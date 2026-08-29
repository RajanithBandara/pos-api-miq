using System;
using POS.Domain.ValueObjects;

namespace POS.Application.Customers.DTOs;

public record CustomerDto(
    Guid Id,
    Guid? StoreId,
    string FirstName,
    string LastName,
    string FullName,
    string? Email,
    string? Phone,
    Address Address,
    decimal LoyaltyPoints,
    decimal StoreCreditBalance,
    bool IsActive,
    long RowVersion);

public record CreateCustomerDto(
    Guid? StoreId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Street,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);

public record UpdateCustomerDto(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Street,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    bool IsActive);
