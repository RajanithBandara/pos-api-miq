using System;
using System.Collections.Generic;
using System.Linq;
using POS.Application.Inventory.DTOs;
using POS.Domain.Entities;
using POS.Domain.Exceptions;

namespace POS.Application.Inventory.Services;

public interface IFifoFefoAllocationStrategy
{
    IReadOnlyList<StockBatchAllocationDto> AllocateBatchesFifo(IEnumerable<StockBatch> batches, decimal requestedQuantity, Guid productId);
    IReadOnlyList<StockBatchAllocationDto> AllocateBatchesFefo(IEnumerable<StockBatch> batches, decimal requestedQuantity, Guid productId);
}

public class FifoFefoAllocationStrategy : IFifoFefoAllocationStrategy
{
    public IReadOnlyList<StockBatchAllocationDto> AllocateBatchesFifo(IEnumerable<StockBatch> batches, decimal requestedQuantity, Guid productId)
    {
        var sortedBatches = batches
            .Where(b => b.IsActive && b.CurrentQuantity > 0)
            .OrderBy(b => b.ReceivedAtUtc)
            .ThenBy(b => b.CreatedAtUtc)
            .ToList();

        return AllocateInternal(sortedBatches, requestedQuantity, productId);
    }

    public IReadOnlyList<StockBatchAllocationDto> AllocateBatchesFefo(IEnumerable<StockBatch> batches, decimal requestedQuantity, Guid productId)
    {
        // FEFO: Batches with nearest expiry date first; if no expiry, fallback to earliest received
        var sortedBatches = batches
            .Where(b => b.IsActive && b.CurrentQuantity > 0)
            .OrderBy(b => b.ExpiryDateUtc.HasValue ? 0 : 1)
            .ThenBy(b => b.ExpiryDateUtc)
            .ThenBy(b => b.ReceivedAtUtc)
            .ToList();

        return AllocateInternal(sortedBatches, requestedQuantity, productId);
    }

    private static IReadOnlyList<StockBatchAllocationDto> AllocateInternal(
        List<StockBatch> sortedBatches,
        decimal requestedQuantity,
        Guid productId)
    {
        var totalAvailable = sortedBatches.Sum(b => b.CurrentQuantity);
        if (totalAvailable < requestedQuantity)
        {
            throw new InsufficientStockException(productId, requestedQuantity, totalAvailable);
        }

        var allocations = new List<StockBatchAllocationDto>();
        var remainingNeeded = requestedQuantity;

        foreach (var batch in sortedBatches)
        {
            if (remainingNeeded <= 0) break;

            var take = Math.Min(batch.CurrentQuantity, remainingNeeded);
            allocations.Add(new StockBatchAllocationDto(
                batch.Id,
                batch.BatchNumber,
                take,
                batch.CostPrice,
                batch.ExpiryDateUtc));

            remainingNeeded -= take;
        }

        return allocations;
    }
}
