using System;
using System.Collections.Generic;

namespace POS.Application.Products.DTOs;

public record ProductDto(
    Guid Id,
    Guid? StoreId,
    Guid? CategoryId,
    string? CategoryName,
    string Sku,
    string Name,
    string? Description,
    decimal CostPrice,
    decimal RetailPrice,
    decimal TaxRate,
    decimal LowStockThreshold,
    bool IsActive,
    bool TrackInventory,
    decimal CurrentStock,
    IReadOnlyList<ProductBarcodeDto> Barcodes,
    long RowVersion);

public record ProductBarcodeDto(
    Guid Id,
    Guid ProductId,
    string Barcode,
    string BarcodeFormat,
    bool IsPrimary);

public record CreateProductDto(
    Guid? StoreId,
    Guid? CategoryId,
    string Sku,
    string Name,
    string? Description,
    decimal CostPrice,
    decimal RetailPrice,
    decimal TaxRate = 0,
    decimal LowStockThreshold = 5,
    bool TrackInventory = true,
    IReadOnlyList<string>? Barcodes = null);

public record UpdateProductDto(
    Guid? CategoryId,
    string Sku,
    string Name,
    string? Description,
    decimal CostPrice,
    decimal RetailPrice,
    decimal TaxRate,
    decimal LowStockThreshold,
    bool IsActive,
    bool TrackInventory);

public record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    string? ParentCategoryName,
    bool IsActive,
    int ProductCount,
    long RowVersion);

public record CreateCategoryDto(
    string Name,
    string? Description,
    Guid? ParentCategoryId);

public record UpdateCategoryDto(
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    bool IsActive);

public record AddBarcodeDto(
    string Barcode,
    string Format = "EAN13",
    bool IsPrimary = false);
