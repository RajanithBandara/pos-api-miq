using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.Analytics.DTOs;
using POS.Application.Common.Models;
using POS.Domain.Enums;
using POS.Domain.Interfaces;

namespace POS.Application.Analytics.Services;

public interface IAnalyticsService
{
    Task<Result<DashboardSummaryDto>> GetDashboardSummaryAsync(Guid storeId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<SalesTrendItemDto>>> GetSalesTrendsAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<TopSellingProductDto>>> GetTopSellingProductsAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, int limit = 10, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<LowStockAlertDto>>> GetLowStockAlertsAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EmployeeSalesPerformanceDto>>> GetEmployeePerformanceAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}

public class AnalyticsService : IAnalyticsService
{
    private readonly ISaleRepository _saleRepository;
    private readonly IProductRepository _productRepository;
    private readonly IStockRepository _stockRepository;
    private readonly IEmployeeRepository _employeeRepository;

    public AnalyticsService(
        ISaleRepository saleRepository,
        IProductRepository productRepository,
        IStockRepository stockRepository,
        IEmployeeRepository employeeRepository)
    {
        _saleRepository = saleRepository;
        _productRepository = productRepository;
        _stockRepository = stockRepository;
        _employeeRepository = employeeRepository;
    }

    public async Task<Result<DashboardSummaryDto>> GetDashboardSummaryAsync(
        Guid storeId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var startOfToday = now.Date;
        var from = fromUtc ?? now.AddDays(-30);
        var to = toUtc ?? now;

        var salesInPeriod = await _saleRepository.GetSalesByDateRangeAsync(storeId, from, to, cancellationToken);
        var validSales = salesInPeriod.Where(s => s.Status == SaleStatus.Completed).ToList();

        var todaySales = validSales.Where(s => s.CompletedAtUtc >= startOfToday).ToList();
        var todaySalesTotal = todaySales.Sum(s => s.GrandTotal);
        var todayCount = todaySales.Count;
        var todayAvg = todayCount > 0 ? todaySalesTotal / todayCount : 0m;

        var periodSalesTotal = validSales.Sum(s => s.GrandTotal);
        var periodCount = validSales.Count;
        var periodAvg = periodCount > 0 ? periodSalesTotal / periodCount : 0m;

        // Inventory valuation
        var stocks = await _stockRepository.FindAsync(s => s.StoreId == storeId, cancellationToken);
        var products = await _productRepository.GetAllAsync(cancellationToken);
        var productDict = products.ToDictionary(p => p.Id);

        decimal totalValuation = 0;
        int lowStockCount = 0;

        foreach (var s in stocks)
        {
            if (productDict.TryGetValue(s.ProductId, out var prod))
            {
                totalValuation += s.QuantityOnHand * prod.CostPrice;
                if (s.QuantityOnHand <= prod.LowStockThreshold)
                {
                    lowStockCount++;
                }
            }
        }

        // Trends
        var trendItems = validSales
            .GroupBy(s => s.CompletedAtUtc.Date)
            .OrderBy(g => g.Key)
            .Select(g => new SalesTrendItemDto(g.Key.ToString("yyyy-MM-dd"), g.Sum(x => x.GrandTotal), g.Count()))
            .ToList();

        // Top products
        var topProducts = validSales
            .SelectMany(s => s.Items)
            .GroupBy(i => new { i.ProductId, i.ProductName, i.Sku })
            .Select(g => new TopSellingProductDto(
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.Sku,
                g.Sum(x => x.Quantity),
                g.Sum(x => x.TotalAmount)))
            .OrderByDescending(x => x.TotalRevenue)
            .Take(5)
            .ToList();

        var summary = new DashboardSummaryDto(
            todaySalesTotal,
            todayCount,
            todayAvg,
            periodSalesTotal,
            periodCount,
            periodAvg,
            totalValuation,
            lowStockCount,
            trendItems,
            topProducts);

        return Result<DashboardSummaryDto>.Success(summary);
    }

    public async Task<Result<IReadOnlyList<SalesTrendItemDto>>> GetSalesTrendsAsync(
        Guid storeId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var sales = await _saleRepository.GetSalesByDateRangeAsync(storeId, fromUtc, toUtc, cancellationToken);
        var trends = sales
            .Where(s => s.Status == SaleStatus.Completed)
            .GroupBy(s => s.CompletedAtUtc.Date)
            .OrderBy(g => g.Key)
            .Select(g => new SalesTrendItemDto(g.Key.ToString("yyyy-MM-dd"), g.Sum(x => x.GrandTotal), g.Count()))
            .ToList();

        return Result<IReadOnlyList<SalesTrendItemDto>>.Success(trends);
    }

    public async Task<Result<IReadOnlyList<TopSellingProductDto>>> GetTopSellingProductsAsync(
        Guid storeId,
        DateTime fromUtc,
        DateTime toUtc,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var sales = await _saleRepository.GetSalesByDateRangeAsync(storeId, fromUtc, toUtc, cancellationToken);
        var top = sales
            .Where(s => s.Status == SaleStatus.Completed)
            .SelectMany(s => s.Items)
            .GroupBy(i => new { i.ProductId, i.ProductName, i.Sku })
            .Select(g => new TopSellingProductDto(
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.Sku,
                g.Sum(x => x.Quantity),
                g.Sum(x => x.TotalAmount)))
            .OrderByDescending(x => x.TotalRevenue)
            .Take(limit)
            .ToList();

        return Result<IReadOnlyList<TopSellingProductDto>>.Success(top);
    }

    public async Task<Result<IReadOnlyList<LowStockAlertDto>>> GetLowStockAlertsAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetLowStockProductsAsync(storeId, cancellationToken);
        var alerts = products.Select(p => new LowStockAlertDto(
            p.Id,
            p.Name,
            p.Sku,
            p.Stocks.FirstOrDefault(s => s.StoreId == storeId)?.QuantityOnHand ?? 0,
            p.LowStockThreshold)).ToList();

        return Result<IReadOnlyList<LowStockAlertDto>>.Success(alerts);
    }

    public async Task<Result<IReadOnlyList<EmployeeSalesPerformanceDto>>> GetEmployeePerformanceAsync(
        Guid storeId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var sales = await _saleRepository.GetSalesByDateRangeAsync(storeId, fromUtc, toUtc, cancellationToken);
        var employees = await _employeeRepository.GetByStoreAsync(storeId, cancellationToken);
        var empDict = employees.ToDictionary(e => e.Id, e => e.FullName);

        var performances = sales
            .Where(s => s.Status == SaleStatus.Completed && s.CashierEmployeeId.HasValue)
            .GroupBy(s => s.CashierEmployeeId!.Value)
            .Select(g =>
            {
                empDict.TryGetValue(g.Key, out var name);
                var total = g.Sum(s => s.GrandTotal);
                var count = g.Count();
                return new EmployeeSalesPerformanceDto(
                    g.Key,
                    name ?? "Unknown",
                    count,
                    total,
                    count > 0 ? total / count : 0m);
            })
            .OrderByDescending(p => p.TotalSalesAmount)
            .ToList();

        return Result<IReadOnlyList<EmployeeSalesPerformanceDto>>.Success(performances);
    }
}
