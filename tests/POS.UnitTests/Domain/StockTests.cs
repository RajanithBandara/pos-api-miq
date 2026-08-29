using System;
using System.Collections.Generic;
using FluentAssertions;
using POS.Application.Inventory.Services;
using POS.Domain.Entities;
using POS.Domain.Exceptions;
using Xunit;

namespace POS.UnitTests.Domain;

public class StockTests
{
    [Fact]
    public void DecreaseStock_WhenStockIsSufficient_ShouldReduceQuantity()
    {
        // Arrange
        var stock = new Stock
        {
            StoreId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            QuantityOnHand = 25m
        };

        // Act
        stock.DecreaseStock(10m);

        // Assert
        stock.QuantityOnHand.Should().Be(15m);
    }

    [Fact]
    public void DecreaseStock_WhenStockIsInsufficient_ShouldThrowInsufficientStockException()
    {
        // Arrange
        var stock = new Stock
        {
            StoreId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            QuantityOnHand = 5m
        };

        // Act
        var act = () => stock.DecreaseStock(10m);

        // Assert
        act.Should().Throw<InsufficientStockException>()
            .WithMessage("*Insufficient stock*");
    }

    [Fact]
    public void StockBatch_DeductQuantity_ShouldDeductAndDeactivateWhenZero()
    {
        // Arrange
        var batch = new StockBatch
        {
            StoreId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            InitialQuantity = 10m,
            CurrentQuantity = 10m,
            IsActive = true
        };

        // Act
        batch.DeductQuantity(10m);

        // Assert
        batch.CurrentQuantity.Should().Be(0m);
        batch.IsActive.Should().BeFalse();
    }
}

public class FifoFefoStrategyTests
{
    private readonly FifoFefoAllocationStrategy _strategy = new();

    [Fact]
    public void AllocateBatchesFifo_ShouldAllocateFromEarliestReceivedBatchFirst()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        var batch1 = new StockBatch
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            ProductId = productId,
            BatchNumber = "BATCH-EARLY",
            CostPrice = 2.0m,
            InitialQuantity = 5,
            CurrentQuantity = 5,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-10),
            IsActive = true
        };

        var batch2 = new StockBatch
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            ProductId = productId,
            BatchNumber = "BATCH-LATER",
            CostPrice = 2.5m,
            InitialQuantity = 10,
            CurrentQuantity = 10,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        };

        var batches = new List<StockBatch> { batch2, batch1 }; // Unsorted input

        // Act
        var allocations = _strategy.AllocateBatchesFifo(batches, 8m, productId);

        // Assert
        allocations.Should().HaveCount(2);
        allocations[0].BatchId.Should().Be(batch1.Id);
        allocations[0].AllocatedQuantity.Should().Be(5m);
        allocations[1].BatchId.Should().Be(batch2.Id);
        allocations[1].AllocatedQuantity.Should().Be(3m);
    }

    [Fact]
    public void AllocateBatchesFefo_ShouldAllocateFromEarliestExpiringBatchFirst()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        var batchLateExp = new StockBatch
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            ProductId = productId,
            BatchNumber = "EXP-LATE",
            CostPrice = 3.0m,
            InitialQuantity = 10,
            CurrentQuantity = 10,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-5),
            ExpiryDateUtc = DateTime.UtcNow.AddDays(60),
            IsActive = true
        };

        var batchSoonExp = new StockBatch
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            ProductId = productId,
            BatchNumber = "EXP-SOON",
            CostPrice = 3.0m,
            InitialQuantity = 4,
            CurrentQuantity = 4,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-1), // Received later but expires sooner!
            ExpiryDateUtc = DateTime.UtcNow.AddDays(10),
            IsActive = true
        };

        var batches = new List<StockBatch> { batchLateExp, batchSoonExp };

        // Act
        var allocations = _strategy.AllocateBatchesFefo(batches, 6m, productId);

        // Assert
        allocations.Should().HaveCount(2);
        allocations[0].BatchId.Should().Be(batchSoonExp.Id);
        allocations[0].AllocatedQuantity.Should().Be(4m);
        allocations[1].BatchId.Should().Be(batchLateExp.Id);
        allocations[1].AllocatedQuantity.Should().Be(2m);
    }
}
