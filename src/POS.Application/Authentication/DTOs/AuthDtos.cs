using System;
using System.Collections.Generic;
using POS.Domain.Enums;

namespace POS.Application.Authentication.DTOs;

public record LoginRequestDto(
    string Username,
    string Password,
    Guid? PosTerminalId = null);

public record LoginResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    UserDto User);

public record RefreshTokenRequestDto(
    string AccessToken,
    string RefreshToken);

public record UserDto(
    Guid Id,
    Guid? StoreId,
    Guid? EmployeeId,
    string Username,
    string Email,
    string FullName,
    UserStatus Status,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public record ChangePasswordRequestDto(
    string CurrentPassword,
    string NewPassword);

public record RegisterUserRequestDto(
    Guid? StoreId,
    Guid? EmployeeId,
    string Username,
    string Email,
    string Password,
    string FullName,
    IReadOnlyList<string> RoleNames);
