using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Products.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Interfaces;

namespace POS.Application.Products.Services;

public interface IProductService
{
    Task<Result<ProductDto>> CreateProductAsync(CreateProductDto request, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> UpdateProductAsync(Guid id, UpdateProductDto request, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> GetProductByBarcodeAsync(string barcode, Guid? storeId = null, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<ProductDto>>> GetProductsPagedAsync(Guid? storeId, Guid? categoryId, string? search, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<Result> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> AddBarcodeAsync(Guid productId, AddBarcodeDto request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<CategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> CreateCategoryAsync(CreateCategoryDto request, CancellationToken cancellationToken = default);
}

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IRepository<Category, Guid> _categoryRepository;
    private readonly IStockRepository _stockRepository;
    private readonly ISyncRepository _syncRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository productRepository,
        IRepository<Category, Guid> categoryRepository,
        IStockRepository stockRepository,
        ISyncRepository syncRepository,
        IUnitOfWork unitOfWork,
        ILogger<ProductService> logger)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _stockRepository = stockRepository;
        _syncRepository = syncRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ProductDto>> CreateProductAsync(CreateProductDto request, CancellationToken cancellationToken = default)
    {
        var existingSku = await _productRepository.GetBySkuAsync(request.Sku, request.StoreId, cancellationToken);
        if (existingSku != null)
            return Result<ProductDto>.Failure($"A product with SKU '{request.Sku}' already exists.", "DUPLICATE_SKU");

        var product = new Product
        {
            StoreId = request.StoreId,
            CategoryId = request.CategoryId,
            Sku = request.Sku.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description,
            CostPrice = request.CostPrice,
            RetailPrice = request.RetailPrice,
            TaxRate = request.TaxRate,
            LowStockThreshold = request.LowStockThreshold,
            TrackInventory = request.TrackInventory,
            IsActive = true,
            RowVersion = 1
        };

        if (request.Barcodes != null)
        {
            bool isFirst = true;
            foreach (var bc in request.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                product.Barcodes.Add(new ProductBarcode
                {
                    ProductId = product.Id,
                    Barcode = bc.Trim(),
                    IsPrimary = isFirst
                });
                isFirst = false;
            }
        }

        await _productRepository.AddAsync(product, cancellationToken);

        // If product belongs to a specific store, initialize stock aggregate
        if (request.StoreId.HasValue)
        {
            var stock = new Stock
            {
                StoreId = request.StoreId.Value,
                ProductId = product.Id,
                QuantityOnHand = 0,
                QuantityReserved = 0,
                QuantityAllocated = 0
            };
            await _stockRepository.AddAsync(stock, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Product created successfully: {Sku} - {Name} (ID: {Id})", product.Sku, product.Name, product.Id);

        var created = await _productRepository.GetWithDetailsAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(MapToDto(created, 0));
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(Guid id, UpdateProductDto request, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetWithDetailsAsync(id, cancellationToken);
        if (product == null)
            return Result<ProductDto>.Failure("Product not found.", "NOT_FOUND");

        product.CategoryId = request.CategoryId;
        product.Sku = request.Sku.Trim();
        product.Name = request.Name.Trim();
        product.Description = request.Description;
        product.CostPrice = request.CostPrice;
        product.RetailPrice = request.RetailPrice;
        product.TaxRate = request.TaxRate;
        product.LowStockThreshold = request.LowStockThreshold;
        product.IsActive = request.IsActive;
        product.TrackInventory = request.TrackInventory;
        product.RowVersion++;

        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Product updated successfully: {Id}", id);
        return Result<ProductDto>.Success(MapToDto(product, 0));
    }

    public async Task<Result<ProductDto>> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetWithDetailsAsync(id, cancellationToken);
        if (product == null)
            return Result<ProductDto>.Failure("Product not found.", "NOT_FOUND");

        decimal stockLevel = product.Stocks.Sum(s => s.QuantityOnHand);
        return Result<ProductDto>.Success(MapToDto(product, stockLevel));
    }

    public async Task<Result<ProductDto>> GetProductByBarcodeAsync(string barcode, Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByBarcodeAsync(barcode, storeId, cancellationToken);
        if (product == null)
            return Result<ProductDto>.Failure($"Product with barcode '{barcode}' not found.", "NOT_FOUND");

        decimal stockLevel = product.Stocks.Sum(s => s.QuantityOnHand);
        return Result<ProductDto>.Success(MapToDto(product, stockLevel));
    }

    public async Task<Result<PagedResult<ProductDto>>> GetProductsPagedAsync(
        Guid? storeId,
        Guid? categoryId,
        string? search,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var allProducts = await _productRepository.GetAllAsync(cancellationToken);
        var query = allProducts.AsEnumerable();

        if (storeId.HasValue)
            query = query.Where(p => p.StoreId == null || p.StoreId == storeId.Value);

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name.ToLowerInvariant().Contains(s) || p.Sku.ToLowerInvariant().Contains(s));
        }

        var list = query.ToList();
        var count = list.Count;
        var paged = list
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(p => MapToDto(p, p.Stocks.Sum(s => s.QuantityOnHand)))
            .ToList();

        return Result<PagedResult<ProductDto>>.Success(new PagedResult<ProductDto>(paged, count, pageNumber, pageSize));
    }

    public async Task<Result> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken);
        if (product == null)
            return Result.Failure("Product not found.", "NOT_FOUND");

        product.IsDeleted = true;
        product.DeletedAtUtc = DateTime.UtcNow;
        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ProductDto>> AddBarcodeAsync(Guid productId, AddBarcodeDto request, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetWithDetailsAsync(productId, cancellationToken);
        if (product == null)
            return Result<ProductDto>.Failure("Product not found.", "NOT_FOUND");

        if (product.Barcodes.Any(b => b.Barcode.Equals(request.Barcode.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Result<ProductDto>.Failure("Barcode already attached to this product.", "DUPLICATE_BARCODE");

        var barcode = new ProductBarcode
        {
            ProductId = product.Id,
            Barcode = request.Barcode.Trim(),
            BarcodeFormat = request.Format,
            IsPrimary = request.IsPrimary || product.Barcodes.Count == 0
        };

        if (barcode.IsPrimary)
        {
            foreach (var b in product.Barcodes) b.IsPrimary = false;
        }

        product.Barcodes.Add(barcode);
        product.RowVersion++;

        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ProductDto>.Success(MapToDto(product, product.Stocks.Sum(s => s.QuantityOnHand)));
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _categoryRepository.GetAllAsync(cancellationToken);
        var dtos = categories.Select(c => new CategoryDto(
            c.Id,
            c.Name,
            c.Description,
            c.ParentCategoryId,
            c.ParentCategory?.Name,
            c.IsActive,
            c.Products.Count,
            c.RowVersion)).ToList();

        return Result<IReadOnlyList<CategoryDto>>.Success(dtos);
    }

    public async Task<Result<CategoryDto>> CreateCategoryAsync(CreateCategoryDto request, CancellationToken cancellationToken = default)
    {
        var category = new Category
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            IsActive = true,
            RowVersion = 1
        };

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CategoryDto>.Success(new CategoryDto(
            category.Id,
            category.Name,
            category.Description,
            category.ParentCategoryId,
            null,
            category.IsActive,
            0,
            category.RowVersion));
    }

    private static ProductDto MapToDto(Product p, decimal currentStock)
    {
        return new ProductDto(
            p.Id,
            p.StoreId,
            p.CategoryId,
            p.Category?.Name,
            p.Sku,
            p.Name,
            p.Description,
            p.CostPrice,
            p.RetailPrice,
            p.TaxRate,
            p.LowStockThreshold,
            p.IsActive,
            p.TrackInventory,
            currentStock,
            p.Barcodes.Select(b => new ProductBarcodeDto(b.Id, b.ProductId, b.Barcode, b.BarcodeFormat, b.IsPrimary)).ToList(),
            p.RowVersion);
    }
}
