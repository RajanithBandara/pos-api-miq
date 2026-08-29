using System;
using System.Collections.Generic;
using POS.Domain.Enums;

namespace POS.Application.Reports.DTOs;

public record ReportFilterDto(
    Guid StoreId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? CategoryId = null,
    Guid? CashierEmployeeId = null,
    PaymentMethod? PaymentMethod = null);

public record SalesReportDto(
    Guid StoreId,
    DateTime FromUtc,
    DateTime ToUtc,
    decimal TotalGrossSales,
    decimal TotalDiscounts,
    decimal TotalTaxes,
    decimal TotalNetSales,
    int TotalTransactions,
    decimal AverageBasketValue,
    IReadOnlyList<PaymentMethodBreakdownDto> PaymentsBreakdown,
    IReadOnlyList<SalesReportLineItemDto> Items);

public record PaymentMethodBreakdownDto(
    PaymentMethod Method,
    decimal TotalAmount,
    int TransactionCount);

public record SalesReportLineItemDto(
    string InvoiceNumber,
    DateTime CompletedAtUtc,
    string? CashierName,
    string? CustomerName,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    SaleStatus Status);

public record InventoryValuationReportDto(
    Guid StoreId,
    DateTime GeneratedAtUtc,
    int TotalProductTypes,
    decimal TotalQuantityOnHand,
    decimal TotalCostValuation,
    decimal TotalRetailValuation,
    decimal PotentialProfit,
    IReadOnlyList<InventoryValuationItemDto> Items);

public record InventoryValuationItemDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    string? CategoryName,
    decimal QuantityOnHand,
    decimal UnitCost,
    decimal UnitRetail,
    decimal TotalCostValue,
    decimal TotalRetailValue);
