using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Authentication.DTOs;
using POS.Application.Common.Interfaces;
using POS.Application.Common.Models;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Interfaces;

namespace POS.Application.Authentication.Services;

public interface IAuthenticationService
{
    Task<Result<LoginResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<LoginResponseDto>> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> RevokeTokenAsync(string refreshToken, string? reason = null, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    Task<Result<UserDto>> RegisterUserAsync(RegisterUserRequestDto request, CancellationToken cancellationToken = default);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IRepository<Role, Guid> _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IUserRepository userRepository,
        IRepository<Role, Guid> roleRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        ILogger<AuthenticationService> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<LoginResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByUsernameWithRolesAsync(request.Username, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Authentication failed: User {Username} not found", request.Username);
            return Result<LoginResponseDto>.Failure("Invalid credentials", "AUTH_INVALID_CREDENTIALS");
        }

        if (user.Status != UserStatus.Active)
        {
            _logger.LogWarning("Authentication failed: User {Username} is inactive/locked (Status: {Status})", request.Username, user.Status);
            return Result<LoginResponseDto>.Failure($"Account is {user.Status}.", "AUTH_ACCOUNT_DISABLED");
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= 5)
            {
                user.Status = UserStatus.LockedOut;
                user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
                _logger.LogWarning("User {Username} locked out after multiple failed attempts", request.Username);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<LoginResponseDto>.Failure("Invalid credentials", "AUTH_INVALID_CREDENTIALS");
        }

        // Reset failed count
        user.AccessFailedCount = 0;
        user.LastLoginAtUtc = DateTime.UtcNow;

        var fullUser = await _userRepository.GetWithRolesAndPermissionsAsync(user.Id, cancellationToken) ?? user;
        var roles = fullUser.UserRoles.Select(ur => ur.Role?.Name ?? string.Empty).Where(r => !string.IsNullOrEmpty(r)).ToList();
        var permissions = fullUser.UserRoles
            .SelectMany(ur => ur.Role?.RolePermissions.Select(rp => rp.Permission?.Code ?? string.Empty) ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        var accessToken = _jwtTokenGenerator.GenerateAccessToken(fullUser, roles, permissions);
        var refreshTokenString = _jwtTokenGenerator.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenString,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        await _userRepository.AddRefreshTokenAsync(refreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {Username} logged in successfully", request.Username);

        var userDto = new UserDto(
            user.Id,
            user.StoreId,
            user.EmployeeId,
            user.Username,
            user.Email,
            user.FullName,
            user.Status,
            roles,
            permissions);

        return Result<LoginResponseDto>.Success(new LoginResponseDto(
            accessToken,
            refreshTokenString,
            DateTime.UtcNow.AddHours(2),
            userDto));
    }

    public async Task<Result<LoginResponseDto>> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        var principal = _jwtTokenGenerator.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
            return Result<LoginResponseDto>.Failure("Invalid access token or signature.", "AUTH_INVALID_TOKEN");

        var tokenRecord = await _userRepository.GetRefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (tokenRecord == null || !tokenRecord.IsActive)
            return Result<LoginResponseDto>.Failure("Invalid or expired refresh token.", "AUTH_INVALID_REFRESH_TOKEN");

        var user = await _userRepository.GetWithRolesAndPermissionsAsync(tokenRecord.UserId, cancellationToken);
        if (user == null || user.Status != UserStatus.Active)
            return Result<LoginResponseDto>.Failure("User account is inactive or not found.", "AUTH_USER_INACTIVE");

        // Revoke old token and rotate
        var newRefreshTokenString = _jwtTokenGenerator.GenerateRefreshToken();
        tokenRecord.RevokedAtUtc = DateTime.UtcNow;
        tokenRecord.ReplacedByToken = newRefreshTokenString;
        tokenRecord.ReasonRevoked = "Token rotation";

        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshTokenString,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        var roles = user.UserRoles.Select(ur => ur.Role?.Name ?? string.Empty).Where(r => !string.IsNullOrEmpty(r)).ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role?.RolePermissions.Select(rp => rp.Permission?.Code ?? string.Empty) ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        var newAccessToken = _jwtTokenGenerator.GenerateAccessToken(user, roles, permissions);

        await _userRepository.AddRefreshTokenAsync(newRefreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Refresh token rotated successfully for user {UserId}", user.Id);

        var userDto = new UserDto(
            user.Id,
            user.StoreId,
            user.EmployeeId,
            user.Username,
            user.Email,
            user.FullName,
            user.Status,
            roles,
            permissions);

        return Result<LoginResponseDto>.Success(new LoginResponseDto(
            newAccessToken,
            newRefreshTokenString,
            DateTime.UtcNow.AddHours(2),
            userDto));
    }

    public async Task<Result> RevokeTokenAsync(string refreshToken, string? reason = null, CancellationToken cancellationToken = default)
    {
        var tokenRecord = await _userRepository.GetRefreshTokenAsync(refreshToken, cancellationToken);
        if (tokenRecord == null)
            return Result.Failure("Token not found.", "NOT_FOUND");

        if (!tokenRecord.IsActive)
            return Result.Failure("Token is already revoked or expired.", "ALREADY_REVOKED");

        tokenRecord.RevokedAtUtc = DateTime.UtcNow;
        tokenRecord.ReasonRevoked = reason ?? "Manual revocation";
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUserService.UserId == null)
            return Result<UserDto>.Failure("User is not authenticated.", "AUTH_UNAUTHENTICATED");

        var user = await _userRepository.GetWithRolesAndPermissionsAsync(_currentUserService.UserId.Value, cancellationToken);
        if (user == null)
            return Result<UserDto>.Failure("User not found.", "NOT_FOUND");

        var roles = user.UserRoles.Select(ur => ur.Role?.Name ?? string.Empty).Where(r => !string.IsNullOrEmpty(r)).ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role?.RolePermissions.Select(rp => rp.Permission?.Code ?? string.Empty) ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        var dto = new UserDto(
            user.Id,
            user.StoreId,
            user.EmployeeId,
            user.Username,
            user.Email,
            user.FullName,
            user.Status,
            roles,
            permissions);

        return Result<UserDto>.Success(dto);
    }

    public async Task<Result<UserDto>> RegisterUserAsync(RegisterUserRequestDto request, CancellationToken cancellationToken = default)
    {
        var existing = await _userRepository.GetByUsernameWithRolesAsync(request.Username, cancellationToken);
        if (existing != null)
            return Result<UserDto>.Failure("Username already exists.", "DUPLICATE_USERNAME");

        var user = new User
        {
            StoreId = request.StoreId,
            EmployeeId = request.EmployeeId,
            Username = request.Username,
            Email = request.Email,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FullName = request.FullName,
            Status = UserStatus.Active
        };

        var allRoles = await _roleRepository.GetAllAsync(cancellationToken);
        foreach (var roleName in request.RoleNames)
        {
            var role = allRoles.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
            if (role != null)
            {
                user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            }
        }

        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {Username} registered successfully with ID {UserId}", user.Username, user.Id);

        return await GetCurrentUserOrById(user.Id, cancellationToken);
    }

    private async Task<Result<UserDto>> GetCurrentUserOrById(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetWithRolesAndPermissionsAsync(userId, cancellationToken);
        if (user == null) return Result<UserDto>.Failure("User not found");

        var roles = user.UserRoles.Select(ur => ur.Role?.Name ?? string.Empty).Where(r => !string.IsNullOrEmpty(r)).ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role?.RolePermissions.Select(rp => rp.Permission?.Code ?? string.Empty) ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        return Result<UserDto>.Success(new UserDto(
            user.Id,
            user.StoreId,
            user.EmployeeId,
            user.Username,
            user.Email,
            user.FullName,
            user.Status,
            roles,
            permissions));
    }
}
