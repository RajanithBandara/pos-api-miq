using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using POS.Application.Authentication.DTOs;
using POS.Application.Authentication.Services;
using POS.Application.Common.Interfaces;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;
using Xunit;

namespace POS.ApplicationTests.Authentication;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRepository<Role, Guid>> _roleRepoMock = new();
    private readonly Mock<IPasswordHasher> _hasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtMock = new();
    private readonly Mock<ICurrentUserService> _currentMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ILogger<AuthenticationService>> _loggerMock = new();

    private readonly AuthenticationService _service;

    public AuthServiceTests()
    {
        _service = new AuthenticationService(
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _hasherMock.Object,
            _jwtMock.Object,
            _currentMock.Object,
            _uowMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnJwtTokenAndUser()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "cashier1",
            PasswordHash = "HASH",
            FullName = "John Doe",
            Status = UserStatus.Active
        };

        _userRepoMock.Setup(r => r.GetByUsernameWithRolesAsync("cashier1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetWithRolesAndPermissionsAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword("password123", "HASH"))
            .Returns(true);
        _jwtMock.Setup(j => j.GenerateAccessToken(It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns("MOCK_ACCESS_TOKEN");
        _jwtMock.Setup(j => j.GenerateRefreshToken())
            .Returns("MOCK_REFRESH_TOKEN");

        // Act
        var result = await _service.LoginAsync(new LoginRequestDto("cashier1", "password123"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.AccessToken.Should().Be("MOCK_ACCESS_TOKEN");
        result.Value.RefreshToken.Should().Be("MOCK_REFRESH_TOKEN");
        result.Value.User.Username.Should().Be("cashier1");
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ShouldFailAndIncrementFailedCount()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "cashier1",
            PasswordHash = "HASH",
            Status = UserStatus.Active,
            AccessFailedCount = 0
        };

        _userRepoMock.Setup(r => r.GetByUsernameWithRolesAsync("cashier1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasherMock.Setup(h => h.VerifyPassword("wrongpass", "HASH"))
            .Returns(false);

        // Act
        var result = await _service.LoginAsync(new LoginRequestDto("cashier1", "wrongpass"));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("AUTH_INVALID_CREDENTIALS");
        user.AccessFailedCount.Should().Be(1);
    }
}
