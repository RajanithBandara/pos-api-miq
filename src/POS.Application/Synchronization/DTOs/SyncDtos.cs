using System;
using System.Collections.Generic;
using POS.Domain.Enums;

namespace POS.Application.Synchronization.DTOs;

public record SyncPushRequestDto(
    Guid PosTerminalId,
    Guid StoreId,
    string IdempotencyKey,
    Guid SyncBatchId,
    DateTime ClientTimestampUtc,
    IReadOnlyList<SyncOperationItemDto> Operations);

public record SyncOperationItemDto(
    Guid ClientOperationId,
    EntityType EntityType,
    Guid EntityId,
    SyncOperationType OperationType,
    long ClientVersion,
    string PayloadJson);

public record SyncPushResponseDto(
    Guid SyncBatchId,
    string IdempotencyKey,
    bool IsSuccess,
    DateTime ServerTimestampUtc,
    long ServerSyncVersion,
    IReadOnlyList<SyncEntityAckDto> AcknowledgedOperations,
    IReadOnlyList<SyncConflictDto> Conflicts,
    string? Message = null);

public record SyncEntityAckDto(
    Guid ClientOperationId,
    Guid EntityId,
    EntityType EntityType,
    SyncStatus Status,
    long NewServerVersion,
    string? Error = null);

public record SyncConflictDto(
    Guid ClientOperationId,
    Guid EntityId,
    EntityType EntityType,
    string ConflictType,
    string Details,
    long ServerVersion,
    long ClientVersion);

public record SyncPullRequestDto(
    Guid StoreId,
    Guid PosTerminalId,
    long LastSyncVersion = 0,
    int BatchSize = 250);

public record SyncPullResponseDto(
    Guid StoreId,
    long CurrentServerVersion,
    long UpToVersion,
    bool HasMore,
    IReadOnlyList<SyncChangeItemDto> Changes);

public record SyncChangeItemDto(
    long Version,
    EntityType EntityType,
    Guid EntityId,
    SyncOperationType OperationType,
    string PayloadJson,
    DateTime TimestampUtc);

// High-level typed payloads transferred inside PayloadJson or directly
public record SyncSalePayload(
    Guid Id,
    Guid StoreId,
    Guid PosTerminalId,
    Guid? CashierEmployeeId,
    Guid? CustomerId,
    string InvoiceNumber,
    decimal SubTotal,
    decimal TaxTotal,
    decimal DiscountTotal,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal ChangeAmount,
    SaleStatus Status,
    string? Notes,
    DateTime CompletedAtUtc,
    string? IdempotencyKey,
    IReadOnlyList<SyncSaleItemPayload> Items,
    IReadOnlyList<SyncPaymentPayload> Payments);

public record SyncSaleItemPayload(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal UnitCost,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxRate,
    decimal TaxAmount,
    decimal TotalAmount);

public record SyncPaymentPayload(
    Guid Id,
    PaymentMethod Method,
    decimal Amount,
    string Currency,
    string? ReferenceNumber,
    PaymentStatus Status,
    DateTime ProcessedAtUtc);

public record SyncCustomerPayload(
    Guid Id,
    Guid? StoreId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Street,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    decimal LoyaltyPoints,
    decimal StoreCreditBalance,
    bool IsActive);

public record SyncStockMovementPayload(
    Guid Id,
    Guid StoreId,
    Guid ProductId,
    Guid? StockBatchId,
    Guid? ReferenceId,
    StockMovementType Type,
    decimal Quantity,
    decimal UnitCost,
    string? Reason,
    Guid? PerformedByUserId,
    DateTime CreatedAtUtc);
