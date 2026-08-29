using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Synchronization.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Interfaces;
using POS.Domain.ValueObjects;

namespace POS.Application.Synchronization.Services;

public interface ISyncEngineService
{
    Task<Result<SyncPushResponseDto>> ProcessPushBatchAsync(SyncPushRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<SyncPullResponseDto>> ProcessPullChangesAsync(SyncPullRequestDto request, CancellationToken cancellationToken = default);
}

public class SyncEngineService : ISyncEngineService
{
    private readonly ISyncRepository _syncRepository;
    private readonly ISaleRepository _saleRepository;
    private readonly IStockRepository _stockRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IProductRepository _productRepository;
    private readonly IRepository<PosTerminal, Guid> _terminalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SyncEngineService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SyncEngineService(
        ISyncRepository syncRepository,
        ISaleRepository saleRepository,
        IStockRepository stockRepository,
        ICustomerRepository customerRepository,
        IProductRepository productRepository,
        IRepository<PosTerminal, Guid> terminalRepository,
        IUnitOfWork unitOfWork,
        ILogger<SyncEngineService> logger)
    {
        _syncRepository = syncRepository;
        _saleRepository = saleRepository;
        _stockRepository = stockRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _terminalRepository = terminalRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SyncPushResponseDto>> ProcessPushBatchAsync(SyncPushRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing Sync Push from Terminal {TerminalId}, Store {StoreId}, Batch {BatchId}, IdempotencyKey: {Key}",
            request.PosTerminalId, request.StoreId, request.SyncBatchId, request.IdempotencyKey);

        // 1. Check Idempotency Record (Network drop retry safe)
        var existingRecord = await _syncRepository.GetIdempotencyRecordAsync(request.PosTerminalId, request.IdempotencyKey, cancellationToken);
        if (existingRecord != null)
        {
            _logger.LogInformation("Idempotent retry detected for Key: {Key}. Returning cached response.", request.IdempotencyKey);
            try
            {
                var cachedResponse = JsonSerializer.Deserialize<SyncPushResponseDto>(existingRecord.ResponseJson, JsonOpts);
                if (cachedResponse != null)
                {
                    return Result<SyncPushResponseDto>.Success(cachedResponse with
                    {
                        Message = "Acknowledged from server idempotency cache (duplicate request)."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached idempotency record response. Re-evaluating.");
            }
        }

        var terminal = await _terminalRepository.GetByIdAsync(request.PosTerminalId, cancellationToken);
        if (terminal == null)
        {
            return Result<SyncPushResponseDto>.Failure($"POS Terminal '{request.PosTerminalId}' not registered.", "TERMINAL_NOT_FOUND");
        }

        var acknowledgments = new List<SyncEntityAckDto>();
        var conflicts = new List<SyncConflictDto>();
        var serverVersion = await _syncRepository.GetLatestVersionAsync(request.StoreId, cancellationToken);

        // 2. Begin Atomic Database Transaction
        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var op in request.Operations)
            {
                try
                {
                    switch (op.EntityType)
                    {
                        case EntityType.Sale:
                            await ProcessSaleSyncOperationAsync(request, op, acknowledgments, cancellationToken);
                            break;

                        case EntityType.Customer:
                            await ProcessCustomerSyncOperationAsync(request, op, acknowledgments, conflicts, cancellationToken);
                            break;

                        case EntityType.StockMovement:
                            await ProcessStockMovementSyncOperationAsync(request, op, acknowledgments, cancellationToken);
                            break;

                        default:
                            _logger.LogWarning("Unsupported sync entity type: {EntityType}", op.EntityType);
                            acknowledgments.Add(new SyncEntityAckDto(
                                op.ClientOperationId,
                                op.EntityId,
                                op.EntityType,
                                SyncStatus.Failed,
                                serverVersion,
                                $"Unsupported sync entity type '{op.EntityType}'"));
                            break;
                    }
                }
                catch (Exception opEx)
                {
                    _logger.LogError(opEx, "Failed to process operation {OperationId} for entity {EntityId}", op.ClientOperationId, op.EntityId);
                    acknowledgments.Add(new SyncEntityAckDto(
                        op.ClientOperationId,
                        op.EntityId,
                        op.EntityType,
                        SyncStatus.Failed,
                        serverVersion,
                        opEx.Message));
                }
            }

            // Update terminal sync metadata
            serverVersion = await _syncRepository.GetLatestVersionAsync(request.StoreId, cancellationToken);
            terminal.LastSyncAtUtc = DateTime.UtcNow;
            terminal.LastSyncVersion = serverVersion;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Construct response
            var responseDto = new SyncPushResponseDto(
                request.SyncBatchId,
                request.IdempotencyKey,
                IsSuccess: conflicts.Count == 0 && acknowledgments.All(a => a.Status != SyncStatus.Failed),
                ServerTimestampUtc: DateTime.UtcNow,
                ServerSyncVersion: serverVersion,
                AcknowledgedOperations: acknowledgments,
                Conflicts: conflicts,
                Message: "Push batch processed successfully.");

            // Store Idempotency Record
            var idempotencyRecord = new SyncIdempotencyRecord
            {
                PosTerminalId = request.PosTerminalId,
                StoreId = request.StoreId,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = ComputeSha256(request.IdempotencyKey + request.SyncBatchId),
                StatusCode = 200,
                ResponseJson = JsonSerializer.Serialize(responseDto, JsonOpts),
                ProcessedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(14)
            };

            await _syncRepository.SaveIdempotencyRecordAsync(idempotencyRecord, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Successfully processed Sync Batch {BatchId} for Terminal {TerminalId}", request.SyncBatchId, request.PosTerminalId);
            return Result<SyncPushResponseDto>.Success(responseDto);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Transaction aborted during Sync Push Batch {BatchId}", request.SyncBatchId);
            return Result<SyncPushResponseDto>.Failure($"Sync push batch failed: {ex.Message}", "SYNC_TRANSACTION_FAILED");
        }
    }

    public async Task<Result<SyncPullResponseDto>> ProcessPullChangesAsync(SyncPullRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing Sync Pull for Store {StoreId}, Terminal {TerminalId}, SinceVersion: {Version}",
            request.StoreId, request.PosTerminalId, request.LastSyncVersion);

        var currentServerVersion = await _syncRepository.GetLatestVersionAsync(request.StoreId, cancellationToken);
        var changeLogs = await _syncRepository.GetChangesSinceVersionAsync(
            request.StoreId,
            request.LastSyncVersion,
            request.BatchSize,
            cancellationToken);

        var changes = changeLogs.Select(c => new SyncChangeItemDto(
            c.Version,
            c.EntityType,
            c.EntityId,
            c.OperationType,
            c.PayloadJson,
            c.CreatedAtUtc)).ToList();

        var upToVersion = changes.Count > 0 ? changes.Max(c => c.Version) : request.LastSyncVersion;
        var hasMore = currentServerVersion > upToVersion;

        var response = new SyncPullResponseDto(
            request.StoreId,
            currentServerVersion,
            upToVersion,
            hasMore,
            changes);

        return Result<SyncPullResponseDto>.Success(response);
    }

    private async Task ProcessSaleSyncOperationAsync(
        SyncPushRequestDto request,
        SyncOperationItemDto op,
        List<SyncEntityAckDto> acknowledgments,
        CancellationToken cancellationToken)
    {
        // 1. Entity-level duplicate check (in case sale ID was already inserted)
        var existingSale = await _saleRepository.GetByIdAsync(op.EntityId, cancellationToken);
        if (existingSale != null)
        {
            _logger.LogInformation("Sale {SaleId} already exists in database. Acknowledging as duplicate.", op.EntityId);
            acknowledgments.Add(new SyncEntityAckDto(
                op.ClientOperationId,
                op.EntityId,
                EntityType.Sale,
                SyncStatus.IgnoredDuplicate,
                existingSale.RowVersion));
            return;
        }

        var salePayload = JsonSerializer.Deserialize<SyncSalePayload>(op.PayloadJson, JsonOpts);
        if (salePayload == null)
        {
            acknowledgments.Add(new SyncEntityAckDto(op.ClientOperationId, op.EntityId, EntityType.Sale, SyncStatus.Failed, 0, "Invalid sale payload JSON."));
            return;
        }

        var sale = new Sale
        {
            Id = salePayload.Id == Guid.Empty ? op.EntityId : salePayload.Id,
            StoreId = request.StoreId,
            PosTerminalId = request.PosTerminalId,
            CashierEmployeeId = salePayload.CashierEmployeeId,
            CustomerId = salePayload.CustomerId,
            InvoiceNumber = salePayload.InvoiceNumber,
            SubTotal = salePayload.SubTotal,
            TaxTotal = salePayload.TaxTotal,
            DiscountTotal = salePayload.DiscountTotal,
            GrandTotal = salePayload.GrandTotal,
            PaidAmount = salePayload.PaidAmount,
            ChangeAmount = salePayload.ChangeAmount,
            Status = salePayload.Status,
            Notes = salePayload.Notes,
            CompletedAtUtc = salePayload.CompletedAtUtc,
            IdempotencyKey = salePayload.IdempotencyKey ?? request.IdempotencyKey,
            SyncBatchId = request.SyncBatchId
        };

        // Add Items
        foreach (var itemPayload in salePayload.Items)
        {
            var item = new SaleItem
            {
                Id = itemPayload.Id == Guid.Empty ? Guid.NewGuid() : itemPayload.Id,
                SaleId = sale.Id,
                ProductId = itemPayload.ProductId,
                Sku = itemPayload.Sku,
                ProductName = itemPayload.ProductName,
                Quantity = itemPayload.Quantity,
                UnitCost = itemPayload.UnitCost,
                UnitPrice = itemPayload.UnitPrice,
                DiscountAmount = itemPayload.DiscountAmount,
                TaxRate = itemPayload.TaxRate,
                TaxAmount = itemPayload.TaxAmount,
                TotalAmount = itemPayload.TotalAmount
            };
            sale.Items.Add(item);

            // Deduct Stock and create StockMovement
            var stock = await _stockRepository.GetByProductAndStoreAsync(item.ProductId, request.StoreId, cancellationToken);
            if (stock != null)
            {
                stock.QuantityOnHand = Math.Max(0, stock.QuantityOnHand - item.Quantity);
            }

            var movement = StockMovement.CreateSaleMovement(
                request.StoreId,
                item.ProductId,
                null,
                sale.Id,
                item.Quantity,
                item.UnitCost,
                null);
            await _stockRepository.AddMovementAsync(movement, cancellationToken);
        }

        // Add Payments
        foreach (var paymentPayload in salePayload.Payments)
        {
            var payment = new Payment
            {
                Id = paymentPayload.Id == Guid.Empty ? Guid.NewGuid() : paymentPayload.Id,
                SaleId = sale.Id,
                Method = paymentPayload.Method,
                Amount = paymentPayload.Amount,
                Currency = paymentPayload.Currency,
                ReferenceNumber = paymentPayload.ReferenceNumber,
                Status = paymentPayload.Status,
                ProcessedAtUtc = paymentPayload.ProcessedAtUtc
            };
            sale.Payments.Add(payment);
        }

        await _saleRepository.AddAsync(sale, cancellationToken);

        // Update Customer points if applicable
        if (sale.CustomerId.HasValue)
        {
            var customer = await _customerRepository.GetByIdAsync(sale.CustomerId.Value, cancellationToken);
            if (customer != null)
            {
                customer.LoyaltyPoints += Math.Floor(sale.GrandTotal);
            }
        }

        acknowledgments.Add(new SyncEntityAckDto(
            op.ClientOperationId,
            sale.Id,
            EntityType.Sale,
            SyncStatus.Success,
            sale.RowVersion));
    }

    private async Task ProcessCustomerSyncOperationAsync(
        SyncPushRequestDto request,
        SyncOperationItemDto op,
        List<SyncEntityAckDto> acknowledgments,
        List<SyncConflictDto> conflicts,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SyncCustomerPayload>(op.PayloadJson, JsonOpts);
        if (payload == null)
        {
            acknowledgments.Add(new SyncEntityAckDto(op.ClientOperationId, op.EntityId, EntityType.Customer, SyncStatus.Failed, 0, "Invalid customer payload."));
            return;
        }

        var customer = await _customerRepository.GetByIdAsync(op.EntityId, cancellationToken);
        if (customer == null)
        {
            customer = new Customer
            {
                Id = op.EntityId,
                StoreId = request.StoreId,
                FirstName = payload.FirstName,
                LastName = payload.LastName,
                Email = payload.Email,
                Phone = payload.Phone,
                Address = new Address(payload.Street ?? "", payload.City ?? "", payload.State ?? "", payload.PostalCode ?? "", payload.Country ?? ""),
                LoyaltyPoints = payload.LoyaltyPoints,
                StoreCreditBalance = payload.StoreCreditBalance,
                IsActive = payload.IsActive,
                RowVersion = 1
            };
            await _customerRepository.AddAsync(customer, cancellationToken);
        }
        else
        {
            // Conflict check: if server version changed since client read
            if (op.ClientVersion > 0 && customer.RowVersion > op.ClientVersion)
            {
                conflicts.Add(new SyncConflictDto(
                    op.ClientOperationId,
                    customer.Id,
                    EntityType.Customer,
                    "VERSION_MISMATCH",
                    $"Server customer version ({customer.RowVersion}) is newer than client version ({op.ClientVersion}).",
                    customer.RowVersion,
                    op.ClientVersion));
                return;
            }

            customer.FirstName = payload.FirstName;
            customer.LastName = payload.LastName;
            customer.Email = payload.Email;
            customer.Phone = payload.Phone;
            customer.Address = new Address(payload.Street ?? "", payload.City ?? "", payload.State ?? "", payload.PostalCode ?? "", payload.Country ?? "");
            customer.LoyaltyPoints = payload.LoyaltyPoints;
            customer.StoreCreditBalance = payload.StoreCreditBalance;
            customer.IsActive = payload.IsActive;
            customer.RowVersion++;
        }

        acknowledgments.Add(new SyncEntityAckDto(
            op.ClientOperationId,
            customer.Id,
            EntityType.Customer,
            SyncStatus.Success,
            customer.RowVersion));
    }

    private async Task ProcessStockMovementSyncOperationAsync(
        SyncPushRequestDto request,
        SyncOperationItemDto op,
        List<SyncEntityAckDto> acknowledgments,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SyncStockMovementPayload>(op.PayloadJson, JsonOpts);
        if (payload == null)
        {
            acknowledgments.Add(new SyncEntityAckDto(op.ClientOperationId, op.EntityId, EntityType.StockMovement, SyncStatus.Failed, 0, "Invalid movement payload."));
            return;
        }

        var movement = new StockMovement
        {
            Id = op.EntityId == Guid.Empty ? Guid.NewGuid() : op.EntityId,
            StoreId = request.StoreId,
            ProductId = payload.ProductId,
            StockBatchId = payload.StockBatchId,
            ReferenceId = payload.ReferenceId,
            Type = payload.Type,
            Quantity = payload.Quantity,
            UnitCost = payload.UnitCost,
            Reason = payload.Reason,
            PerformedByUserId = payload.PerformedByUserId,
            CreatedAtUtc = payload.CreatedAtUtc
        };

        var stock = await _stockRepository.GetByProductAndStoreAsync(payload.ProductId, request.StoreId, cancellationToken);
        if (stock != null)
        {
            stock.QuantityOnHand += payload.Quantity;
        }

        await _stockRepository.AddMovementAsync(movement, cancellationToken);

        acknowledgments.Add(new SyncEntityAckDto(
            op.ClientOperationId,
            movement.Id,
            EntityType.StockMovement,
            SyncStatus.Success,
            1));
    }

    private static string ComputeSha256(string rawData)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes);
    }
}
