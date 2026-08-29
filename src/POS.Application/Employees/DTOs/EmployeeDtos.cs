using System;
using POS.Domain.Enums;

namespace POS.Application.Employees.DTOs;

public record EmployeeDto(
    Guid Id,
    Guid StoreId,
    string? StoreName,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string FullName,
    string? Email,
    string? Phone,
    string? RoleTitle,
    EmployeeStatus Status,
    decimal? HourlyRate,
    DateTime? HiredAtUtc,
    DateTime? TerminatedAtUtc,
    Guid? UserId,
    string? Username);

public record CreateEmployeeDto(
    Guid StoreId,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? RoleTitle,
    decimal? HourlyRate,
    DateTime? HiredAtUtc = null);

public record UpdateEmployeeDto(
    Guid StoreId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? RoleTitle,
    EmployeeStatus Status,
    decimal? HourlyRate);
