using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using POS.Application.Common.Models;
using POS.Application.Synchronization.DTOs;
using POS.Application.Synchronization.Services;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;
using Xunit;

namespace POS.ApplicationTests.Synchronization;

public class SyncEngineServiceTests
{
    private readonly Mock<ISyncRepository> _syncRepoMock = new();
    private readonly Mock<ISaleRepository> _saleRepoMock = new();
    private readonly Mock<IStockRepository> _stockRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<IProductRepository> _productRepoMock = new();
    private readonly Mock<IRepository<PosTerminal, Guid>> _terminalRepoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ITransactionScope> _txMock = new();
    private readonly Mock<ILogger<SyncEngineService>> _loggerMock = new();

    private readonly SyncEngineService _service;

    public SyncEngineServiceTests()
    {
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_txMock.Object);

        _service = new SyncEngineService(
            _syncRepoMock.Object,
            _saleRepoMock.Object,
            _stockRepoMock.Object,
            _customerRepoMock.Object,
            _productRepoMock.Object,
            _terminalRepoMock.Object,
            _uowMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessPushBatchAsync_WhenIdempotencyKeyAlreadyExists_ShouldReturnCachedResponseImmediately()
    {
        // Arrange
        var terminalId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var idempotencyKey = "SYNC-KEY-999";

        var cachedResponseDto = new SyncPushResponseDto(
            batchId,
            idempotencyKey,
            IsSuccess: true,
            ServerTimestampUtc: DateTime.UtcNow.AddMinutes(-5),
            ServerSyncVersion: 42,
            AcknowledgedOperations: new List<SyncEntityAckDto>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), EntityType.Sale, SyncStatus.Success, 42)
            },
            Conflicts: new List<SyncConflictDto>());

        var existingRecord = new SyncIdempotencyRecord
        {
            PosTerminalId = terminalId,
            StoreId = storeId,
            IdempotencyKey = idempotencyKey,
            ResponseJson = JsonSerializer.Serialize(cachedResponseDto)
        };

        _syncRepoMock.Setup(s => s.GetIdempotencyRecordAsync(terminalId, idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecord);

        var request = new SyncPushRequestDto(
            terminalId,
            storeId,
            idempotencyKey,
            batchId,
            DateTime.UtcNow,
            new List<SyncOperationItemDto>());

        // Act
        var result = await _service.ProcessPushBatchAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ServerSyncVersion.Should().Be(42);
        result.Value.Message.Should().Contain("idempotency cache");

        // Verify that NO database transaction or new inserts were executed
        _uowMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _saleRepoMock.Verify(s => s.AddAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPushBatchAsync_WhenSaleAlreadyInDb_ShouldReturnIgnoredDuplicateAcknowledgment()
    {
        // Arrange
        var terminalId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var opId = Guid.NewGuid();

        _terminalRepoMock.Setup(t => t.GetByIdAsync(terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PosTerminal { Id = terminalId, StoreId = storeId });

        // Existing sale in DB
        _saleRepoMock.Setup(s => s.GetByIdAsync(saleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sale { Id = saleId, StoreId = storeId, RowVersion = 5 });

        var operation = new SyncOperationItemDto(
            opId,
            EntityType.Sale,
            saleId,
            SyncOperationType.Insert,
            1,
            "{}");

        var request = new SyncPushRequestDto(
            terminalId,
            storeId,
            "KEY-DUPLICATE-SALE",
            batchId,
            DateTime.UtcNow,
            new List<SyncOperationItemDto> { operation });

        // Act
        var result = await _service.ProcessPushBatchAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AcknowledgedOperations.Should().ContainSingle(a =>
            a.EntityId == saleId &&
            a.Status == SyncStatus.IgnoredDuplicate);

        _saleRepoMock.Verify(s => s.AddAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
