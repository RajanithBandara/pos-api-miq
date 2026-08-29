using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Application.Sales.DTOs;
using POS.Application.Sales.Services;

namespace POS.Api.Controllers;

public class SalesController : ApiControllerBase
{
    private readonly ISaleService _saleService;

    public SalesController(ISaleService saleService)
    {
        _saleService = saleService;
    }

    [Authorize(Policy = Permissions.ProcessSales)]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SaleDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> CreateSale([FromBody] CreateSaleRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _saleService.CreateSaleAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [Authorize(Policy = Permissions.ViewSales)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SaleDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetSaleById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _saleService.GetSaleByIdAsync(id, cancellationToken);
        return HandleResult(result);
    }

    [Authorize(Policy = Permissions.ViewSales)]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SaleSummaryDto>>), 200)]
    public async Task<IActionResult> GetSales(
        [FromQuery] Guid storeId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _saleService.GetSalesPagedAsync(storeId, fromUtc, toUtc, pageNumber, pageSize, cancellationToken);
        return HandleResult(result);
    }

    [Authorize(Policy = Permissions.ProcessSales)]
    [HttpPost("{id:guid}/void")]
    [ProducesResponseType(typeof(ApiResponse<SaleDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> VoidSale(Guid id, [FromBody] string reason, CancellationToken cancellationToken)
    {
        var result = await _saleService.VoidSaleAsync(id, reason, cancellationToken);
        return HandleResult(result);
    }
}
