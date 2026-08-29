using System;
using POS.Domain.Common;

namespace POS.Domain.Events;

public record SaleCompletedDomainEvent(
    Guid SaleId,
    Guid StoreId,
    Guid PosTerminalId,
    string InvoiceNumber,
    decimal GrandTotal,
    DateTime OccurredOnUtc) : IDomainEvent;

public record StockMovementRecordedDomainEvent(
    Guid MovementId,
    Guid StoreId,
    Guid ProductId,
    decimal Quantity,
    DateTime OccurredOnUtc) : IDomainEvent;

public record ProductPriceUpdatedDomainEvent(
    Guid ProductId,
    decimal OldPrice,
    decimal NewPrice,
    DateTime OccurredOnUtc) : IDomainEvent;
