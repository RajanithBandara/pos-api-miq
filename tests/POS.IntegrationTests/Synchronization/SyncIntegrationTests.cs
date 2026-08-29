using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.Authentication.DTOs;
using POS.Application.Common.Models;
using POS.Application.Synchronization.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.ValueObjects;
using POS.Infrastructure.Persistence;
using POS.IntegrationTests.Infrastructure;
using Xunit;

using System.Text.Json.Serialization;

namespace POS.IntegrationTests.Synchronization;

public class SyncIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SyncIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> AuthenticateAsSuperAdminAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto("admin", "Admin@123456"));
        loginResponse.EnsureSuccessStatusCode();

        var content = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(JsonOpts);
        return content!.Data!.AccessToken;
    }

    [Fact]
    public async Task SyncPush_WhenSaleSentAndRetriedDueToNetworkLoss_ShouldNotDuplicateSaleInDatabase()
    {
        // 1. Authenticate
        var token = await AuthenticateAsSuperAdminAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Setup Test Store, Terminal, Product, and Stock in DB
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var idempotencyKey = "RETRY-IDEM-KEY-" + Guid.NewGuid();
        var syncBatchId = Guid.NewGuid();
        var saleId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var store = new Store { Id = storeId, Code = "STORE-TEST-1", Name = "Test Store" };
            var terminal = new PosTerminal { Id = terminalId, StoreId = storeId, TerminalCode = "TERM-TEST-1", TerminalName = "Term 1" };
            var product = new Product { Id = productId, StoreId = storeId, Sku = "SKU-SYNC-1", Name = "Sync Product 1", CostPrice = 5, RetailPrice = 10, TrackInventory = true };
            var stock = new Stock { StoreId = storeId, ProductId = productId, QuantityOnHand = 50 };

            db.Stores.Add(store);
            db.PosTerminals.Add(terminal);
            db.Products.Add(product);
            db.Stocks.Add(stock);
            await db.SaveChangesAsync();
        }

        // 3. Construct Offline Sale Payload
        var salePayload = new SyncSalePayload(
            Id: saleId,
            StoreId: storeId,
            PosTerminalId: terminalId,
            CashierEmployeeId: null,
            CustomerId: null,
            InvoiceNumber: "INV-OFFLINE-001",
            SubTotal: 20m,
            TaxTotal: 0m,
            DiscountTotal: 0m,
            GrandTotal: 20m,
            PaidAmount: 20m,
            ChangeAmount: 0m,
            Status: SaleStatus.Completed,
            Notes: "Offline sale",
            CompletedAtUtc: DateTime.UtcNow,
            IdempotencyKey: idempotencyKey,
            Items: new List<SyncSaleItemPayload>
            {
                new(Guid.NewGuid(), productId, "SKU-SYNC-1", "Sync Product 1", 2m, 5m, 10m, 0m, 0m, 0m, 20m)
            },
            Payments: new List<SyncPaymentPayload>
            {
                new(Guid.NewGuid(), PaymentMethod.Cash, 20m, "USD", "CASH-REF", PaymentStatus.Completed, DateTime.UtcNow)
            });

        var pushRequest = new SyncPushRequestDto(
            PosTerminalId: terminalId,
            StoreId: storeId,
            IdempotencyKey: idempotencyKey,
            SyncBatchId: syncBatchId,
            ClientTimestampUtc: DateTime.UtcNow,
            Operations: new List<SyncOperationItemDto>
            {
                new(Guid.NewGuid(), EntityType.Sale, saleId, SyncOperationType.Insert, 1, JsonSerializer.Serialize(salePayload))
            });

        // 4. Initial Sync Push
        var firstResponse = await _client.PostAsJsonAsync("/api/sync/push", pushRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstResult = await firstResponse.Content.ReadFromJsonAsync<ApiResponse<SyncPushResponseDto>>(JsonOpts);
        firstResult!.Success.Should().BeTrue();
        firstResult.Data!.IsSuccess.Should().BeTrue();

        // 5. Simulate Network Loss and POS Retry with EXACT SAME IdempotencyKey
        var retryResponse = await _client.PostAsJsonAsync("/api/sync/push", pushRequest);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retryResult = await retryResponse.Content.ReadFromJsonAsync<ApiResponse<SyncPushResponseDto>>(JsonOpts);
        retryResult!.Success.Should().BeTrue();
        retryResult.Data!.IsSuccess.Should().BeTrue();
        retryResult.Data.Message.Should().Contain("idempotency cache");

        // 6. Verify Database State: Exactly ONE sale must exist in PostgreSQL
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var matchingSales = await db.Sales
                .Include(s => s.Items)
                .Include(s => s.Payments)
                .Where(s => s.StoreId == storeId && s.IdempotencyKey == idempotencyKey)
                .ToListAsync();

            matchingSales.Should().HaveCount(1, "The sale must NOT be duplicated after retry!");
            matchingSales[0].GrandTotal.Should().Be(20m);
            matchingSales[0].Items.Should().HaveCount(1);
            matchingSales[0].Payments.Should().HaveCount(1);

            var stock = await db.Stocks.FirstOrDefaultAsync(s => s.StoreId == storeId && s.ProductId == productId);
            stock.Should().NotBeNull();
            stock!.QuantityOnHand.Should().Be(48m, "Stock should only be deducted once!");
        }
    }

    [Fact]
    public async Task SyncPush_MultipleOfflineTransactions_ShouldPersistAllValidTransactions()
    {
        // 1. Authenticate
        var token = await AuthenticateAsSuperAdminAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Setup Data
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Stores.Add(new Store { Id = storeId, Code = "STORE-MULTI", Name = "Multi-Tx Store" });
            db.PosTerminals.Add(new PosTerminal { Id = terminalId, StoreId = storeId, TerminalCode = "TERM-MULTI", TerminalName = "Term Multi" });
            db.Products.Add(new Product { Id = productId, StoreId = storeId, Sku = "SKU-MULTI", Name = "Multi Item", CostPrice = 1, RetailPrice = 2, TrackInventory = true });
            db.Stocks.Add(new Stock { StoreId = storeId, ProductId = productId, QuantityOnHand = 100 });
            await db.SaveChangesAsync();
        }

        // 3. Create Multi-Operation Payload: 1 Customer creation + 1 Sale
        var customerPayload = new SyncCustomerPayload(
            Id: customerId,
            StoreId: storeId,
            FirstName: "Alice",
            LastName: "Smith",
            Email: "alice.smith@example.com",
            Phone: "+15550199",
            Street: "456 Oak St",
            City: "Seattle",
            State: "WA",
            PostalCode: "98101",
            Country: "USA",
            LoyaltyPoints: 0,
            StoreCreditBalance: 0,
            IsActive: true);

        var saleId = Guid.NewGuid();
        var salePayload = new SyncSalePayload(
            Id: saleId,
            StoreId: storeId,
            PosTerminalId: terminalId,
            CashierEmployeeId: null,
            CustomerId: customerId,
            InvoiceNumber: "INV-MULTI-001",
            SubTotal: 10m,
            TaxTotal: 0m,
            DiscountTotal: 0m,
            GrandTotal: 10m,
            PaidAmount: 10m,
            ChangeAmount: 0m,
            Status: SaleStatus.Completed,
            Notes: null,
            CompletedAtUtc: DateTime.UtcNow,
            IdempotencyKey: "MULTI-SALE-KEY",
            Items: new List<SyncSaleItemPayload>
            {
                new(Guid.NewGuid(), productId, "SKU-MULTI", "Multi Item", 5m, 1m, 2m, 0m, 0m, 0m, 10m)
            },
            Payments: new List<SyncPaymentPayload>
            {
                new(Guid.NewGuid(), PaymentMethod.Card, 10m, "USD", "TX-12345", PaymentStatus.Completed, DateTime.UtcNow)
            });

        var request = new SyncPushRequestDto(
            PosTerminalId: terminalId,
            StoreId: storeId,
            IdempotencyKey: "BATCH-IDEM-" + Guid.NewGuid(),
            SyncBatchId: batchId,
            ClientTimestampUtc: DateTime.UtcNow,
            Operations: new List<SyncOperationItemDto>
            {
                new(Guid.NewGuid(), EntityType.Customer, customerId, SyncOperationType.Insert, 1, JsonSerializer.Serialize(customerPayload)),
                new(Guid.NewGuid(), EntityType.Sale, saleId, SyncOperationType.Insert, 1, JsonSerializer.Serialize(salePayload))
            });

        // 4. Send Push
        var response = await _client.PostAsJsonAsync("/api/sync/push", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SyncPushResponseDto>>(JsonOpts);
        result!.Success.Should().BeTrue();
        result.Data!.AcknowledgedOperations.Should().HaveCount(2);
        result.Data.AcknowledgedOperations.Should().OnlyContain(a => a.Status == SyncStatus.Success);

        // 5. Verify Database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var savedCustomer = await db.Customers.FindAsync(customerId);
            savedCustomer.Should().NotBeNull();
            savedCustomer!.FullName.Should().Be("Alice Smith");

            var savedSale = await db.Sales.FindAsync(saleId);
            savedSale.Should().NotBeNull();
            savedSale!.GrandTotal.Should().Be(10m);
        }
    }
}
