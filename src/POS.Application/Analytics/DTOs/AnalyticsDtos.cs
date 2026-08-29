using System;
using System.Collections.Generic;

namespace POS.Application.Analytics.DTOs;

public record DashboardSummaryDto(
    decimal TodaySalesTotal,
    int TodayTransactionsCount,
    decimal TodayAverageTransactionValue,
    decimal TotalRevenueInPeriod,
    int TotalTransactionsInPeriod,
    decimal AverageTransactionValueInPeriod,
    decimal TotalInventoryValuation,
    int LowStockItemCount,
    IReadOnlyList<SalesTrendItemDto> RecentSalesTrend,
    IReadOnlyList<TopSellingProductDto> TopProducts);

public record SalesTrendItemDto(
    string DateLabel,
    decimal TotalSales,
    int TransactionCount);

public record TopSellingProductDto(
    Guid ProductId,
    string ProductName,
    string Sku,
    decimal TotalQuantitySold,
    decimal TotalRevenue);

public record LowStockAlertDto(
    Guid ProductId,
    string ProductName,
    string Sku,
    decimal CurrentStock,
    decimal LowStockThreshold);

public record EmployeeSalesPerformanceDto(
    Guid EmployeeId,
    string EmployeeName,
    int TransactionCount,
    decimal TotalSalesAmount,
    decimal AverageSaleAmount);

public record StoreSalesSummaryDto(
    Guid StoreId,
    string StoreName,
    decimal TotalSales,
    int TransactionCount,
    decimal AverageTransactionValue);
