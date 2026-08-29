using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Customers.DTOs;
using POS.Domain.Entities;
using POS.Domain.Interfaces;
using POS.Domain.ValueObjects;

namespace POS.Application.Customers.Services;

public interface ICustomerService
{
    Task<Result<CustomerDto>> CreateCustomerAsync(CreateCustomerDto request, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> UpdateCustomerAsync(Guid id, UpdateCustomerDto request, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<CustomerDto>>> SearchCustomersAsync(string query, Guid? storeId = null, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<CustomerDto>>> GetCustomersPagedAsync(Guid? storeId, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(
        ICustomerRepository customerRepository,
        IUnitOfWork unitOfWork,
        ILogger<CustomerService> logger)
    {
        _customerRepository = customerRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CustomerDto>> CreateCustomerAsync(CreateCustomerDto request, CancellationToken cancellationToken = default)
    {
        var existing = await _customerRepository.GetByPhoneOrEmailAsync(request.Phone, request.Email, request.StoreId, cancellationToken);
        if (existing != null)
            return Result<CustomerDto>.Failure("Customer with this email or phone number already exists.", "DUPLICATE_CUSTOMER");

        var customer = new Customer
        {
            StoreId = request.StoreId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            Address = new Address(request.Street ?? "", request.City ?? "", request.State ?? "", request.PostalCode ?? "", request.Country ?? ""),
            LoyaltyPoints = 0,
            StoreCreditBalance = 0,
            IsActive = true,
            RowVersion = 1
        };

        await _customerRepository.AddAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Customer created: {FullName} (ID: {Id})", customer.FullName, customer.Id);
        return Result<CustomerDto>.Success(MapToDto(customer));
    }

    public async Task<Result<CustomerDto>> UpdateCustomerAsync(Guid id, UpdateCustomerDto request, CancellationToken cancellationToken = default)
    {
        var customer = await _customerRepository.GetByIdAsync(id, cancellationToken);
        if (customer == null)
            return Result<CustomerDto>.Failure("Customer not found.", "NOT_FOUND");

        customer.FirstName = request.FirstName.Trim();
        customer.LastName = request.LastName.Trim();
        customer.Email = request.Email?.Trim();
        customer.Phone = request.Phone?.Trim();
        customer.Address = new Address(request.Street ?? "", request.City ?? "", request.State ?? "", request.PostalCode ?? "", request.Country ?? "");
        customer.IsActive = request.IsActive;
        customer.RowVersion++;

        _customerRepository.Update(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CustomerDto>.Success(MapToDto(customer));
    }

    public async Task<Result<CustomerDto>> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _customerRepository.GetByIdAsync(id, cancellationToken);
        if (customer == null)
            return Result<CustomerDto>.Failure("Customer not found.", "NOT_FOUND");

        return Result<CustomerDto>.Success(MapToDto(customer));
    }

    public async Task<Result<IReadOnlyList<CustomerDto>>> SearchCustomersAsync(string query, Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        var customers = await _customerRepository.SearchCustomersAsync(query, storeId, 25, cancellationToken);
        var dtos = customers.Select(MapToDto).ToList();
        return Result<IReadOnlyList<CustomerDto>>.Success(dtos);
    }

    public async Task<Result<PagedResult<CustomerDto>>> GetCustomersPagedAsync(Guid? storeId, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var all = await _customerRepository.GetAllAsync(cancellationToken);
        var filtered = storeId.HasValue ? all.Where(c => c.StoreId == null || c.StoreId == storeId.Value).ToList() : all.ToList();

        var count = filtered.Count;
        var paged = filtered
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();

        return Result<PagedResult<CustomerDto>>.Success(new PagedResult<CustomerDto>(paged, count, pageNumber, pageSize));
    }

    private static CustomerDto MapToDto(Customer c)
    {
        return new CustomerDto(
            c.Id,
            c.StoreId,
            c.FirstName,
            c.LastName,
            c.FullName,
            c.Email,
            c.Phone,
            c.Address,
            c.LoyaltyPoints,
            c.StoreCreditBalance,
            c.IsActive,
            c.RowVersion);
    }
}
