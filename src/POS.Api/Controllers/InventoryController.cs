using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Application.Inventory.DTOs;
using POS.Application.Inventory.Services;

namespace POS.Api.Controllers;

[Authorize(Policy = Permissions.ManageInventory)]
public class InventoryController : ApiControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("stores/{storeId:guid}/stock")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StockDto>>), 200)]
    public async Task<IActionResult> GetStoreStock(Guid storeId, CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetStockLevelsByStoreAsync(storeId, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("stores/{storeId:guid}/products/{productId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<StockDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetProductStock(Guid storeId, Guid productId, CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetStockLevelAsync(storeId, productId, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("stores/{storeId:guid}/products/{productId:guid}/batches")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StockBatchDto>>), 200)]
    public async Task<IActionResult> GetProductBatches(Guid storeId, Guid productId, CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetProductBatchesAsync(storeId, productId, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("stores/{storeId:guid}/expiring")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StockBatchDto>>), 200)]
    public async Task<IActionResult> GetExpiringBatches(Guid storeId, [FromQuery] int withinDays = 30, CancellationToken cancellationToken = default)
    {
        var result = await _inventoryService.GetExpiringBatchesAsync(storeId, withinDays, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("receive")]
    [ProducesResponseType(typeof(ApiResponse<StockBatchDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> ReceiveStock([FromBody] ReceiveStockRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _inventoryService.ReceiveStockBatchAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("adjust")]
    [ProducesResponseType(typeof(ApiResponse<StockDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> AdjustStock([FromBody] StockAdjustmentRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _inventoryService.AdjustStockAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("stores/{storeId:guid}/products/{productId:guid}/movements")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StockMovementDto>>), 200)]
    public async Task<IActionResult> GetMovements(Guid storeId, Guid productId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var result = await _inventoryService.GetMovementsAsync(storeId, productId, limit, cancellationToken);
        return HandleResult(result);
    }
}
