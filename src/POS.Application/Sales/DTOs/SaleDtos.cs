using System;
using System.Collections.Generic;
using POS.Domain.Enums;

namespace POS.Application.Sales.DTOs;

public record CreateSaleRequestDto(
    Guid StoreId,
    Guid PosTerminalId,
    Guid? CashierEmployeeId,
    Guid? CustomerId,
    string? IdempotencyKey,
    string? Notes,
    IReadOnlyList<CreateSaleItemRequestDto> Items,
    IReadOnlyList<CreatePaymentRequestDto> Payments);

public record CreateSaleItemRequestDto(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount = 0);

public record CreatePaymentRequestDto(
    PaymentMethod Method,
    decimal Amount,
    string Currency = "USD",
    string? ReferenceNumber = null);

public record SaleDto(
    Guid Id,
    Guid StoreId,
    Guid PosTerminalId,
    Guid? CashierEmployeeId,
    string? CashierName,
    Guid? CustomerId,
    string? CustomerName,
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
    DateTime CreatedAtUtc,
    IReadOnlyList<SaleItemDto> Items,
    IReadOnlyList<PaymentDto> Payments);

public record SaleItemDto(
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

public record PaymentDto(
    Guid Id,
    PaymentMethod Method,
    decimal Amount,
    string Currency,
    string? ReferenceNumber,
    PaymentStatus Status,
    DateTime ProcessedAtUtc);

public record SaleSummaryDto(
    Guid Id,
    string InvoiceNumber,
    Guid StoreId,
    string? CustomerName,
    decimal GrandTotal,
    SaleStatus Status,
    DateTime CompletedAtUtc);
