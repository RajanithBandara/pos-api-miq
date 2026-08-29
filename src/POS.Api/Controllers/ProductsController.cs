using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Application.Products.DTOs;
using POS.Application.Products.Services;

namespace POS.Api.Controllers;

public class ProductsController : ApiControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [Authorize(Policy = Permissions.ManageProducts)]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductDto request, CancellationToken cancellationToken)
    {
        var result = await _productService.CreateProductAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _productService.GetProductByIdAsync(id, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("barcode/{barcode}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetProductByBarcode(string barcode, [FromQuery] Guid? storeId, CancellationToken cancellationToken)
    {
        var result = await _productService.GetProductByBarcodeAsync(barcode, storeId, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProductDto>>), 200)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] Guid? storeId,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _productService.GetProductsPagedAsync(storeId, categoryId, search, pageNumber, pageSize, cancellationToken);
        return HandleResult(result);
    }

    [Authorize(Policy = Permissions.ManageProducts)]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductDto request, CancellationToken cancellationToken)
    {
        var result = await _productService.UpdateProductAsync(id, request, cancellationToken);
        return HandleResult(result);
    }

    [Authorize(Policy = Permissions.ManageProducts)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var result = await _productService.DeleteProductAsync(id, cancellationToken);
        return HandleResult(result);
    }

    [Authorize(Policy = Permissions.ManageProducts)]
    [HttpPost("{id:guid}/barcodes")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    public async Task<IActionResult> AddBarcode(Guid id, [FromBody] AddBarcodeDto request, CancellationToken cancellationToken)
    {
        var result = await _productService.AddBarcodeAsync(id, request, cancellationToken);
        return HandleResult(result);
    }
}

public class CategoriesController : ApiControllerBase
{
    private readonly IProductService _productService;

    public CategoriesController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CategoryDto>>), 200)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var result = await _productService.GetCategoriesAsync(cancellationToken);
        return HandleResult(result);
    }

    [Authorize(Policy = Permissions.ManageProducts)]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), 200)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryDto request, CancellationToken cancellationToken)
    {
        var result = await _productService.CreateCategoryAsync(request, cancellationToken);
        return HandleResult(result);
    }
}
