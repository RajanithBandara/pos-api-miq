using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Analytics.DTOs;
using POS.Application.Analytics.Services;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Application.Reports.DTOs;
using POS.Application.Reports.Services;

namespace POS.Api.Controllers;

[Authorize(Policy = Permissions.ViewDashboard)]
public class AnalyticsController : ApiControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<DashboardSummaryDto>), 200)]
    public async Task<IActionResult> GetDashboardSummary(
        [FromQuery] Guid storeId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var result = await _analyticsService.GetDashboardSummaryAsync(storeId, fromUtc, toUtc, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("sales-trends")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SalesTrendItemDto>>), 200)]
    public async Task<IActionResult> GetSalesTrends(
        [FromQuery] Guid storeId,
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var result = await _analyticsService.GetSalesTrendsAsync(storeId, fromUtc, toUtc, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("top-products")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TopSellingProductDto>>), 200)]
    public async Task<IActionResult> GetTopProducts(
        [FromQuery] Guid storeId,
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _analyticsService.GetTopSellingProductsAsync(storeId, fromUtc, toUtc, limit, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("low-stock")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LowStockAlertDto>>), 200)]
    public async Task<IActionResult> GetLowStock(
        [FromQuery] Guid storeId,
        CancellationToken cancellationToken)
    {
        var result = await _analyticsService.GetLowStockAlertsAsync(storeId, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("employee-performance")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeSalesPerformanceDto>>), 200)]
    public async Task<IActionResult> GetEmployeePerformance(
        [FromQuery] Guid storeId,
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var result = await _analyticsService.GetEmployeePerformanceAsync(storeId, fromUtc, toUtc, cancellationToken);
        return HandleResult(result);
    }
}

[Authorize(Policy = Permissions.ViewReports)]
public class ReportsController : ApiControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpPost("sales")]
    [ProducesResponseType(typeof(ApiResponse<SalesReportDto>), 200)]
    public async Task<IActionResult> GenerateSalesReport(
        [FromBody] ReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await _reportService.GenerateSalesReportAsync(filter, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("inventory-valuation/{storeId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<InventoryValuationReportDto>), 200)]
    public async Task<IActionResult> GenerateInventoryReport(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var result = await _reportService.GenerateInventoryValuationReportAsync(storeId, cancellationToken);
        return HandleResult(result);
    }
}

[AllowAnonymous]
public class HealthController : ApiControllerBase
{
    [HttpGet("/health")]
    [ProducesResponseType(200)]
    public IActionResult Check()
    {
        return Ok(new
        {
            status = "Healthy",
            timestampUtc = DateTime.UtcNow,
            version = "1.0.0",
            service = "POS.Api"
        });
    }
}
