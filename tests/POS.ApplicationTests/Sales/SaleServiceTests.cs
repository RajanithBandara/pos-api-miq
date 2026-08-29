using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using POS.Application.Sales.DTOs;
using POS.Application.Sales.Services;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Interfaces;
using Xunit;

namespace POS.ApplicationTests.Sales;

public class SaleServiceTests
{
    private readonly Mock<ISaleRepository> _saleRepoMock = new();
    private readonly Mock<IProductRepository> _productRepoMock = new();
    private readonly Mock<IStockRepository> _stockRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<IRepository<Employee, Guid>> _employeeRepoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ITransactionScope> _txMock = new();
    private readonly Mock<ILogger<SaleService>> _loggerMock = new();

    private readonly SaleService _service;

    public SaleServiceTests()
    {
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_txMock.Object);

        _service = new SaleService(
            _saleRepoMock.Object,
            _productRepoMock.Object,
            _stockRepoMock.Object,
            _customerRepoMock.Object,
            _employeeRepoMock.Object,
            _uowMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CreateSaleAsync_WhenStockIsInsufficient_ShouldThrowInsufficientStockExceptionAndRollback()
    {
        // Arrange
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            Name = "Coffee Beans",
            Sku = "COF-01",
            CostPrice = 5m,
            RetailPrice = 12m,
            TrackInventory = true
        };

        var stock = new Stock
        {
            StoreId = storeId,
            ProductId = productId,
            QuantityOnHand = 2m // Only 2 in stock
        };

        _saleRepoMock.Setup(s => s.GenerateNextInvoiceNumberAsync(storeId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-2026-0001");
        _productRepoMock.Setup(p => p.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _stockRepoMock.Setup(s => s.GetByProductAndStoreAsync(productId, storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

        var request = new CreateSaleRequestDto(
            storeId,
            terminalId,
            null,
            null,
            null,
            null,
            new List<CreateSaleItemRequestDto>
            {
                new(productId, 5m, 12m) // Requesting 5 items
            },
            new List<CreatePaymentRequestDto>
            {
                new(PaymentMethod.Cash, 60m)
            });

        // Act
        var act = () => _service.CreateSaleAsync(request);

        // Assert
        await act.Should().ThrowAsync<InsufficientStockException>();
        _txMock.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
