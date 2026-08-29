using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Inventory.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Interfaces;

namespace POS.Application.Inventory.Services;

public interface IInventoryService
{
    Task<Result<StockDto>> GetStockLevelAsync(Guid storeId, Guid productId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StockDto>>> GetStockLevelsByStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StockBatchDto>>> GetProductBatchesAsync(Guid storeId, Guid productId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StockBatchDto>>> GetExpiringBatchesAsync(Guid storeId, int withinDays = 30, CancellationToken cancellationToken = default);
    Task<Result<StockBatchDto>> ReceiveStockBatchAsync(ReceiveStockRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<StockDto>> AdjustStockAsync(StockAdjustmentRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StockMovementDto>>> GetMovementsAsync(Guid storeId, Guid productId, int limit = 50, CancellationToken cancellationToken = default);
}

public class InventoryService : IInventoryService
{
    private readonly IStockRepository _stockRepository;
    private readonly IProductRepository _productRepository;
    private readonly IFifoFefoAllocationStrategy _allocationStrategy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IStockRepository stockRepository,
        IProductRepository productRepository,
        IFifoFefoAllocationStrategy allocationStrategy,
        IUnitOfWork unitOfWork,
        ILogger<InventoryService> logger)
    {
        _stockRepository = stockRepository;
        _productRepository = productRepository;
        _allocationStrategy = allocationStrategy;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<StockDto>> GetStockLevelAsync(Guid storeId, Guid productId, CancellationToken cancellationToken = default)
    {
        var stock = await _stockRepository.GetByProductAndStoreAsync(productId, storeId, cancellationToken);
        if (stock == null)
            return Result<StockDto>.Failure("Stock record not found for product in store.", "NOT_FOUND");

        var product = await _productRepository.GetByIdAsync(productId, cancellationToken);
        return Result<StockDto>.Success(new StockDto(
            stock.Id,
            stock.StoreId,
            stock.ProductId,
            product?.Name ?? "Unknown Product",
            product?.Sku ?? "N/A",
            stock.QuantityOnHand,
            stock.QuantityReserved,
            stock.QuantityAllocated,
            stock.AvailableQuantity,
            stock.LastCountAtUtc,
            stock.RowVersion));
    }

    public async Task<Result<IReadOnlyList<StockDto>>> GetStockLevelsByStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var stocks = await _stockRepository.FindAsync(s => s.StoreId == storeId, cancellationToken);
        var productIds = stocks.Select(s => s.ProductId).Distinct().ToList();
        var allProducts = await _productRepository.GetAllAsync(cancellationToken);
        var productDict = allProducts.ToDictionary(p => p.Id);

        var list = stocks.Select(s =>
        {
            productDict.TryGetValue(s.ProductId, out var product);
            return new StockDto(
                s.Id,
                s.StoreId,
                s.ProductId,
                product?.Name ?? "Unknown Product",
                product?.Sku ?? "N/A",
                s.QuantityOnHand,
                s.QuantityReserved,
                s.QuantityAllocated,
                s.AvailableQuantity,
                s.LastCountAtUtc,
                s.RowVersion);
        }).ToList();

        return Result<IReadOnlyList<StockDto>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<StockBatchDto>>> GetProductBatchesAsync(Guid storeId, Guid productId, CancellationToken cancellationToken = default)
    {
        var batches = await _stockRepository.GetAvailableBatchesAsync(productId, storeId, cancellationToken);
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken);

        var dtos = batches.Select(b => new StockBatchDto(
            b.Id,
            b.StoreId,
            b.ProductId,
            product?.Name ?? "Unknown",
            product?.Sku ?? "N/A",
            b.BatchNumber,
            b.CostPrice,
            b.InitialQuantity,
            b.CurrentQuantity,
            b.ExpiryDateUtc,
            b.ReceivedAtUtc,
            b.IsActive)).ToList();

        return Result<IReadOnlyList<StockBatchDto>>.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<StockBatchDto>>> GetExpiringBatchesAsync(Guid storeId, int withinDays = 30, CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow.AddDays(withinDays);
        var batches = await _stockRepository.GetExpiringBatchesAsync(storeId, threshold, cancellationToken);
        var allProducts = await _productRepository.GetAllAsync(cancellationToken);
        var productDict = allProducts.ToDictionary(p => p.Id);

        var dtos = batches.Select(b =>
        {
            productDict.TryGetValue(b.ProductId, out var product);
            return new StockBatchDto(
                b.Id,
                b.StoreId,
                b.ProductId,
                product?.Name ?? "Unknown",
                product?.Sku ?? "N/A",
                b.BatchNumber,
                b.CostPrice,
                b.InitialQuantity,
                b.CurrentQuantity,
                b.ExpiryDateUtc,
                b.ReceivedAtUtc,
                b.IsActive);
        }).ToList();

        return Result<IReadOnlyList<StockBatchDto>>.Success(dtos);
    }

    public async Task<Result<StockBatchDto>> ReceiveStockBatchAsync(ReceiveStockRequestDto request, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product == null)
            return Result<StockBatchDto>.Failure("Product not found.", "NOT_FOUND");

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var batch = new StockBatch
            {
                StoreId = request.StoreId,
                ProductId = request.ProductId,
                BatchNumber = request.BatchNumber.Trim(),
                CostPrice = request.CostPrice,
                InitialQuantity = request.Quantity,
                CurrentQuantity = request.Quantity,
                ExpiryDateUtc = request.ExpiryDateUtc,
                ReceivedAtUtc = DateTime.UtcNow,
                IsActive = true
            };

            await _stockRepository.AddBatchAsync(batch, cancellationToken);

            var stock = await _stockRepository.GetByProductAndStoreAsync(request.ProductId, request.StoreId, cancellationToken);
            if (stock == null)
            {
                stock = new Stock
                {
                    StoreId = request.StoreId,
                    ProductId = request.ProductId,
                    QuantityOnHand = request.Quantity
                };
                await _stockRepository.AddAsync(stock, cancellationToken);
            }
            else
            {
                stock.IncreaseStock(request.Quantity);
                _stockRepository.Update(stock);
            }

            var movement = new StockMovement
            {
                StoreId = request.StoreId,
                ProductId = request.ProductId,
                StockBatchId = batch.Id,
                Type = StockMovementType.Purchase,
                Quantity = request.Quantity,
                UnitCost = request.CostPrice,
                Reason = $"Received stock batch {batch.BatchNumber}",
                CreatedAtUtc = DateTime.UtcNow
            };

            await _stockRepository.AddMovementAsync(movement, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Stock batch received: {BatchNumber}, Qty: {Qty} for product {ProductId}", batch.BatchNumber, batch.InitialQuantity, product.Id);

            return Result<StockBatchDto>.Success(new StockBatchDto(
                batch.Id,
                batch.StoreId,
                batch.ProductId,
                product.Name,
                product.Sku,
                batch.BatchNumber,
                batch.CostPrice,
                batch.InitialQuantity,
                batch.CurrentQuantity,
                batch.ExpiryDateUtc,
                batch.ReceivedAtUtc,
                batch.IsActive));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to receive stock batch {BatchNumber}", request.BatchNumber);
            throw;
        }
    }

    public async Task<Result<StockDto>> AdjustStockAsync(StockAdjustmentRequestDto request, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product == null)
            return Result<StockDto>.Failure("Product not found.", "NOT_FOUND");

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var stock = await _stockRepository.GetByProductAndStoreAsync(request.ProductId, request.StoreId, cancellationToken);
            if (stock == null)
            {
                stock = new Stock { StoreId = request.StoreId, ProductId = request.ProductId, QuantityOnHand = 0 };
                await _stockRepository.AddAsync(stock, cancellationToken);
            }

            if (request.AdjustedQuantity > 0)
            {
                stock.IncreaseStock(request.AdjustedQuantity);
            }
            else
            {
                stock.DecreaseStock(Math.Abs(request.AdjustedQuantity));
            }

            if (request.StockBatchId.HasValue)
            {
                var batch = (await _stockRepository.GetAvailableBatchesAsync(request.ProductId, request.StoreId, cancellationToken))
                    .FirstOrDefault(b => b.Id == request.StockBatchId.Value);

                if (batch != null)
                {
                    if (request.AdjustedQuantity > 0)
                        batch.AddQuantity(request.AdjustedQuantity);
                    else
                        batch.DeductQuantity(Math.Abs(request.AdjustedQuantity));
                }
            }

            var movement = new StockMovement
            {
                StoreId = request.StoreId,
                ProductId = request.ProductId,
                StockBatchId = request.StockBatchId,
                Type = request.Type,
                Quantity = request.AdjustedQuantity,
                UnitCost = product.CostPrice,
                Reason = request.Reason,
                CreatedAtUtc = DateTime.UtcNow
            };

            _stockRepository.Update(stock);
            await _stockRepository.AddMovementAsync(movement, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Result<StockDto>.Success(new StockDto(
                stock.Id,
                stock.StoreId,
                stock.ProductId,
                product.Name,
                product.Sku,
                stock.QuantityOnHand,
                stock.QuantityReserved,
                stock.QuantityAllocated,
                stock.AvailableQuantity,
                stock.LastCountAtUtc,
                stock.RowVersion));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to adjust stock for product {ProductId}", request.ProductId);
            throw;
        }
    }

    public async Task<Result<IReadOnlyList<StockMovementDto>>> GetMovementsAsync(Guid storeId, Guid productId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var movements = await _stockRepository.GetMovementsByProductAsync(productId, storeId, limit, cancellationToken);
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken);

        var dtos = movements.Select(m => new StockMovementDto(
            m.Id,
            m.StoreId,
            m.ProductId,
            product?.Name ?? "Unknown",
            product?.Sku ?? "N/A",
            m.StockBatchId,
            m.StockBatch?.BatchNumber,
            m.ReferenceId,
            m.Type,
            m.Quantity,
            m.UnitCost,
            m.Reason,
            m.PerformedByUserId,
            m.CreatedAtUtc)).ToList();

        return Result<IReadOnlyList<StockMovementDto>>.Success(dtos);
    }
}
