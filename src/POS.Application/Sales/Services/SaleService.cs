using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Sales.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Interfaces;

namespace POS.Application.Sales.Services;

public interface ISaleService
{
    Task<Result<SaleDto>> CreateSaleAsync(CreateSaleRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<SaleDto>> GetSaleByIdAsync(Guid saleId, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<SaleSummaryDto>>> GetSalesPagedAsync(Guid storeId, DateTime? fromUtc, DateTime? toUtc, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<Result<SaleDto>> VoidSaleAsync(Guid saleId, string reason, CancellationToken cancellationToken = default);
}

public class SaleService : ISaleService
{
    private readonly ISaleRepository _saleRepository;
    private readonly IProductRepository _productRepository;
    private readonly IStockRepository _stockRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SaleService> _logger;

    public SaleService(
        ISaleRepository saleRepository,
        IProductRepository productRepository,
        IStockRepository stockRepository,
        ICustomerRepository customerRepository,
        IRepository<Employee, Guid> employeeRepository,
        IUnitOfWork unitOfWork,
        ILogger<SaleService> logger)
    {
        _saleRepository = saleRepository;
        _productRepository = productRepository;
        _stockRepository = stockRepository;
        _customerRepository = customerRepository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SaleDto>> CreateSaleAsync(CreateSaleRequestDto request, CancellationToken cancellationToken = default)
    {
        // 1. Idempotency Check if key is provided
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingSale = await _saleRepository.GetByIdempotencyKeyAsync(request.StoreId, request.IdempotencyKey, cancellationToken);
            if (existingSale != null)
            {
                _logger.LogInformation("Sale with IdempotencyKey {Key} already exists. Returning existing sale {SaleId}", request.IdempotencyKey, existingSale.Id);
                return Result<SaleDto>.Success(MapToDto(existingSale));
            }
        }

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var invoiceNumber = await _saleRepository.GenerateNextInvoiceNumberAsync(request.StoreId, request.PosTerminalId, cancellationToken);

            var sale = new Sale
            {
                StoreId = request.StoreId,
                PosTerminalId = request.PosTerminalId,
                CashierEmployeeId = request.CashierEmployeeId,
                CustomerId = request.CustomerId,
                InvoiceNumber = invoiceNumber,
                Notes = request.Notes,
                IdempotencyKey = request.IdempotencyKey,
                CompletedAtUtc = DateTime.UtcNow
            };

            foreach (var itemReq in request.Items)
            {
                var product = await _productRepository.GetByIdAsync(itemReq.ProductId, cancellationToken);
                if (product == null)
                    throw new EntityNotFoundException(nameof(Product), itemReq.ProductId);

                var saleItem = new SaleItem
                {
                    SaleId = sale.Id,
                    ProductId = product.Id,
                    Sku = product.Sku,
                    ProductName = product.Name,
                    Quantity = itemReq.Quantity,
                    UnitCost = product.CostPrice,
                    UnitPrice = itemReq.UnitPrice > 0 ? itemReq.UnitPrice : product.RetailPrice,
                    DiscountAmount = itemReq.DiscountAmount,
                    TaxRate = product.TaxRate
                };
                saleItem.CalculateItemTotal();
                sale.AddItem(saleItem);

                // Inventory allocation
                if (product.TrackInventory)
                {
                    var stock = await _stockRepository.GetByProductAndStoreAsync(product.Id, request.StoreId, cancellationToken);
                    if (stock == null || stock.AvailableQuantity < itemReq.Quantity)
                    {
                        throw new InsufficientStockException(product.Id, itemReq.Quantity, stock?.AvailableQuantity ?? 0);
                    }

                    stock.DecreaseStock(itemReq.Quantity);
                    _stockRepository.Update(stock);

                    var movement = StockMovement.CreateSaleMovement(
                        request.StoreId,
                        product.Id,
                        null,
                        sale.Id,
                        itemReq.Quantity,
                        product.CostPrice,
                        null);
                    await _stockRepository.AddMovementAsync(movement, cancellationToken);
                }
            }

            foreach (var payReq in request.Payments)
            {
                var payment = new Payment
                {
                    SaleId = sale.Id,
                    Method = payReq.Method,
                    Amount = payReq.Amount,
                    Currency = payReq.Currency,
                    ReferenceNumber = payReq.ReferenceNumber,
                    Status = PaymentStatus.Completed,
                    ProcessedAtUtc = DateTime.UtcNow
                };
                sale.AddPayment(payment);
            }

            sale.MarkCompleted();

            // Customer Loyalty Points
            if (sale.CustomerId.HasValue)
            {
                var customer = await _customerRepository.GetByIdAsync(sale.CustomerId.Value, cancellationToken);
                if (customer != null)
                {
                    customer.LoyaltyPoints += Math.Floor(sale.GrandTotal);
                    _customerRepository.Update(customer);
                }
            }

            await _saleRepository.AddAsync(sale, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Sale {InvoiceNumber} (ID: {SaleId}) created successfully", sale.InvoiceNumber, sale.Id);

            var createdSale = await _saleRepository.GetWithDetailsAsync(sale.Id, cancellationToken) ?? sale;
            return Result<SaleDto>.Success(MapToDto(createdSale));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create sale in store {StoreId}", request.StoreId);
            throw;
        }
    }

    public async Task<Result<SaleDto>> GetSaleByIdAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var sale = await _saleRepository.GetWithDetailsAsync(saleId, cancellationToken);
        if (sale == null)
            return Result<SaleDto>.Failure("Sale not found.", "NOT_FOUND");

        return Result<SaleDto>.Success(MapToDto(sale));
    }

    public async Task<Result<PagedResult<SaleSummaryDto>>> GetSalesPagedAsync(
        Guid storeId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc ?? DateTime.UtcNow.AddDays(-30);
        var to = toUtc ?? DateTime.UtcNow;

        var sales = await _saleRepository.GetSalesByDateRangeAsync(storeId, from, to, cancellationToken);
        var count = sales.Count;

        var pagedItems = sales
            .OrderByDescending(s => s.CompletedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SaleSummaryDto(
                s.Id,
                s.InvoiceNumber,
                s.StoreId,
                s.Customer?.FullName,
                s.GrandTotal,
                s.Status,
                s.CompletedAtUtc))
            .ToList();

        var result = new PagedResult<SaleSummaryDto>(pagedItems, count, pageNumber, pageSize);
        return Result<PagedResult<SaleSummaryDto>>.Success(result);
    }

    public async Task<Result<SaleDto>> VoidSaleAsync(Guid saleId, string reason, CancellationToken cancellationToken = default)
    {
        var sale = await _saleRepository.GetWithDetailsAsync(saleId, cancellationToken);
        if (sale == null)
            return Result<SaleDto>.Failure("Sale not found.", "NOT_FOUND");

        if (sale.Status == SaleStatus.Voided)
            return Result<SaleDto>.Failure("Sale is already voided.", "ALREADY_VOIDED");

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            sale.VoidSale(reason);

            // Revert inventory
            foreach (var item in sale.Items)
            {
                var stock = await _stockRepository.GetByProductAndStoreAsync(item.ProductId, sale.StoreId, cancellationToken);
                if (stock != null)
                {
                    stock.IncreaseStock(item.Quantity);
                    _stockRepository.Update(stock);
                }

                var movement = new StockMovement
                {
                    StoreId = sale.StoreId,
                    ProductId = item.ProductId,
                    ReferenceId = sale.Id,
                    Type = StockMovementType.Return,
                    Quantity = item.Quantity,
                    UnitCost = item.UnitCost,
                    Reason = $"Voided Sale {sale.InvoiceNumber}: {reason}",
                    CreatedAtUtc = DateTime.UtcNow
                };
                await _stockRepository.AddMovementAsync(movement, cancellationToken);
            }

            _saleRepository.Update(sale);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Result<SaleDto>.Success(MapToDto(sale));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to void sale {SaleId}", saleId);
            throw;
        }
    }

    private static SaleDto MapToDto(Sale sale)
    {
        return new SaleDto(
            sale.Id,
            sale.StoreId,
            sale.PosTerminalId,
            sale.CashierEmployeeId,
            sale.CashierEmployee?.FullName,
            sale.CustomerId,
            sale.Customer?.FullName,
            sale.InvoiceNumber,
            sale.SubTotal,
            sale.TaxTotal,
            sale.DiscountTotal,
            sale.GrandTotal,
            sale.PaidAmount,
            sale.ChangeAmount,
            sale.Status,
            sale.Notes,
            sale.CompletedAtUtc,
            sale.CreatedAtUtc,
            sale.Items.Select(i => new SaleItemDto(
                i.Id,
                i.ProductId,
                i.Sku,
                i.ProductName,
                i.Quantity,
                i.UnitCost,
                i.UnitPrice,
                i.DiscountAmount,
                i.TaxRate,
                i.TaxAmount,
                i.TotalAmount)).ToList(),
            sale.Payments.Select(p => new PaymentDto(
                p.Id,
                p.Method,
                p.Amount,
                p.Currency,
                p.ReferenceNumber,
                p.Status,
                p.ProcessedAtUtc)).ToList());
    }
}
