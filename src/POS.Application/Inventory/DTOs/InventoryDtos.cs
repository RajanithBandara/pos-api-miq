using System;
using System.Collections.Generic;
using POS.Domain.Enums;

namespace POS.Application.Inventory.DTOs;

public record StockDto(
    Guid Id,
    Guid StoreId,
    Guid ProductId,
    string ProductName,
    string Sku,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityAllocated,
    decimal AvailableQuantity,
    DateTime? LastCountAtUtc,
    long RowVersion);

public record StockBatchDto(
    Guid Id,
    Guid StoreId,
    Guid ProductId,
    string ProductName,
    string Sku,
    string BatchNumber,
    decimal CostPrice,
    decimal InitialQuantity,
    decimal CurrentQuantity,
    DateTime? ExpiryDateUtc,
    DateTime ReceivedAtUtc,
    bool IsActive);

public record StockMovementDto(
    Guid Id,
    Guid StoreId,
    Guid ProductId,
    string ProductName,
    string Sku,
    Guid? StockBatchId,
    string? BatchNumber,
    Guid? ReferenceId,
    StockMovementType Type,
    decimal Quantity,
    decimal UnitCost,
    string? Reason,
    Guid? PerformedByUserId,
    DateTime CreatedAtUtc);

public record StockAdjustmentRequestDto(
    Guid StoreId,
    Guid ProductId,
    Guid? StockBatchId,
    decimal AdjustedQuantity,
    StockMovementType Type,
    string Reason);

public record ReceiveStockRequestDto(
    Guid StoreId,
    Guid ProductId,
    string BatchNumber,
    decimal CostPrice,
    decimal Quantity,
    DateTime? ExpiryDateUtc = null);

public record StockBatchAllocationDto(
    Guid BatchId,
    string BatchNumber,
    decimal AllocatedQuantity,
    decimal UnitCost,
    DateTime? ExpiryDateUtc);
