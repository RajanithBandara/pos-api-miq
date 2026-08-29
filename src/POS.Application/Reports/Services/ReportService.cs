using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.Common.Models;
using POS.Application.Reports.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;

namespace POS.Application.Reports.Services;

public interface IReportService
{
    Task<Result<SalesReportDto>> GenerateSalesReportAsync(ReportFilterDto filter, CancellationToken cancellationToken = default);
    Task<Result<InventoryValuationReportDto>> GenerateInventoryValuationReportAsync(Guid storeId, CancellationToken cancellationToken = default);
}

public class ReportService : IReportService
{
    private readonly ISaleRepository _saleRepository;
    private readonly IProductRepository _productRepository;
    private readonly IStockRepository _stockRepository;
    private readonly IEmployeeRepository _employeeRepository;

    public ReportService(
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

    public async Task<Result<SalesReportDto>> GenerateSalesReportAsync(ReportFilterDto filter, CancellationToken cancellationToken = default)
    {
        var sales = await _saleRepository.GetSalesByDateRangeAsync(filter.StoreId, filter.FromUtc, filter.ToUtc, cancellationToken);
        var query = sales.AsEnumerable();

        if (filter.CashierEmployeeId.HasValue)
            query = query.Where(s => s.CashierEmployeeId == filter.CashierEmployeeId.Value);

        var list = query.ToList();
        var completedSales = list.Where(s => s.Status == SaleStatus.Completed).ToList();

        var totalGross = completedSales.Sum(s => s.SubTotal);
        var totalDiscounts = completedSales.Sum(s => s.DiscountTotal);
        var totalTaxes = completedSales.Sum(s => s.TaxTotal);
        var totalNet = completedSales.Sum(s => s.GrandTotal);
        var count = completedSales.Count;
        var avgBasket = count > 0 ? totalNet / count : 0m;

        var paymentsBreakdown = completedSales
            .SelectMany(s => s.Payments)
            .GroupBy(p => p.Method)
            .Select(g => new PaymentMethodBreakdownDto(g.Key, g.Sum(x => x.Amount), g.Count()))
            .ToList();

        var lineItems = list.Select(s => new SalesReportLineItemDto(
            s.InvoiceNumber,
            s.CompletedAtUtc,
            s.CashierEmployee?.FullName,
            s.Customer?.FullName,
            s.SubTotal,
            s.DiscountTotal,
            s.TaxTotal,
            s.GrandTotal,
            s.Status)).ToList();

        var report = new SalesReportDto(
            filter.StoreId,
            filter.FromUtc,
            filter.ToUtc,
            totalGross,
            totalDiscounts,
            totalTaxes,
            totalNet,
            count,
            avgBasket,
            paymentsBreakdown,
            lineItems);

        return Result<SalesReportDto>.Success(report);
    }

    public async Task<Result<InventoryValuationReportDto>> GenerateInventoryValuationReportAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var stocks = await _stockRepository.FindAsync(s => s.StoreId == storeId, cancellationToken);
        var products = await _productRepository.GetAllAsync(cancellationToken);
        var productDict = products.ToDictionary(p => p.Id);

        var items = new List<InventoryValuationItemDto>();
        decimal totalCost = 0;
        decimal totalRetail = 0;
        decimal totalQty = 0;

        foreach (var s in stocks)
        {
            if (productDict.TryGetValue(s.ProductId, out var p))
            {
                var costVal = s.QuantityOnHand * p.CostPrice;
                var retailVal = s.QuantityOnHand * p.RetailPrice;

                totalCost += costVal;
                totalRetail += retailVal;
                totalQty += s.QuantityOnHand;

                items.Add(new InventoryValuationItemDto(
                    p.Id,
                    p.Sku,
                    p.Name,
                    p.Category?.Name,
                    s.QuantityOnHand,
                    p.CostPrice,
                    p.RetailPrice,
                    costVal,
                    retailVal));
            }
        }

        var report = new InventoryValuationReportDto(
            storeId,
            DateTime.UtcNow,
            items.Count,
            totalQty,
            totalCost,
            totalRetail,
            totalRetail - totalCost,
            items);

        return Result<InventoryValuationReportDto>.Success(report);
    }
}
