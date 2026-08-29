using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Employees.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;

namespace POS.Application.Employees.Services;

public interface IEmployeeService
{
    Task<Result<EmployeeDto>> CreateEmployeeAsync(CreateEmployeeDto request, CancellationToken cancellationToken = default);
    Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid id, UpdateEmployeeDto request, CancellationToken cancellationToken = default);
    Task<Result<EmployeeDto>> GetEmployeeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EmployeeDto>>> GetEmployeesByStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeDto>> ChangeStatusAsync(Guid id, EmployeeStatus newStatus, CancellationToken cancellationToken = default);
}

public class EmployeeService : IEmployeeService
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IRepository<Store, Guid> _storeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeService(
        IEmployeeRepository employeeRepository,
        IRepository<Store, Guid> storeRepository,
        IUnitOfWork unitOfWork,
        ILogger<EmployeeService> logger)
    {
        _employeeRepository = employeeRepository;
        _storeRepository = storeRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<EmployeeDto>> CreateEmployeeAsync(CreateEmployeeDto request, CancellationToken cancellationToken = default)
    {
        var existing = await _employeeRepository.GetByCodeAsync(request.StoreId, request.EmployeeCode, cancellationToken);
        if (existing != null)
            return Result<EmployeeDto>.Failure($"Employee with code '{request.EmployeeCode}' already exists in this store.", "DUPLICATE_CODE");

        var employee = new Employee
        {
            StoreId = request.StoreId,
            EmployeeCode = request.EmployeeCode.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            RoleTitle = request.RoleTitle?.Trim(),
            Status = EmployeeStatus.Active,
            HourlyRate = request.HourlyRate,
            HiredAtUtc = request.HiredAtUtc ?? DateTime.UtcNow
        };

        await _employeeRepository.AddAsync(employee, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Employee created: {Code} - {FullName} (ID: {Id})", employee.EmployeeCode, employee.FullName, employee.Id);
        return Result<EmployeeDto>.Success(MapToDto(employee));
    }

    public async Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid id, UpdateEmployeeDto request, CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        if (employee == null)
            return Result<EmployeeDto>.Failure("Employee not found.", "NOT_FOUND");

        employee.StoreId = request.StoreId;
        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.Email = request.Email?.Trim();
        employee.Phone = request.Phone?.Trim();
        employee.RoleTitle = request.RoleTitle?.Trim();
        employee.Status = request.Status;
        employee.HourlyRate = request.HourlyRate;

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<EmployeeDto>.Success(MapToDto(employee));
    }

    public async Task<Result<EmployeeDto>> GetEmployeeByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        if (employee == null)
            return Result<EmployeeDto>.Failure("Employee not found.", "NOT_FOUND");

        return Result<EmployeeDto>.Success(MapToDto(employee));
    }

    public async Task<Result<IReadOnlyList<EmployeeDto>>> GetEmployeesByStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var employees = await _employeeRepository.GetByStoreAsync(storeId, cancellationToken);
        var dtos = employees.Select(MapToDto).ToList();
        return Result<IReadOnlyList<EmployeeDto>>.Success(dtos);
    }

    public async Task<Result<EmployeeDto>> ChangeStatusAsync(Guid id, EmployeeStatus newStatus, CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        if (employee == null)
            return Result<EmployeeDto>.Failure("Employee not found.", "NOT_FOUND");

        employee.Status = newStatus;
        if (newStatus == EmployeeStatus.Terminated)
        {
            employee.TerminatedAtUtc = DateTime.UtcNow;
        }

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<EmployeeDto>.Success(MapToDto(employee));
    }

    private static EmployeeDto MapToDto(Employee e)
    {
        return new EmployeeDto(
            e.Id,
            e.StoreId,
            e.Store?.Name,
            e.EmployeeCode,
            e.FirstName,
            e.LastName,
            e.FullName,
            e.Email,
            e.Phone,
            e.RoleTitle,
            e.Status,
            e.HourlyRate,
            e.HiredAtUtc,
            e.TerminatedAtUtc,
            e.User?.Id,
            e.User?.Username);
    }
}
