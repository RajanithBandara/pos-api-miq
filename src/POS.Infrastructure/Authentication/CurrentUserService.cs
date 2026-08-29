using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using POS.Application.Common.Interfaces;

namespace POS.Infrastructure.Authentication;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var idClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
            return Guid.TryParse(idClaim, out var id) ? id : null;
        }
    }

    public string? Username => User?.FindFirstValue(ClaimTypes.Name) ?? User?.FindFirstValue("unique_name");

    public Guid? StoreId
    {
        get
        {
            var storeClaim = User?.FindFirstValue("storeId");
            return Guid.TryParse(storeClaim, out var id) ? id : null;
        }
    }

    public Guid? PosTerminalId
    {
        get
        {
            var termClaim = User?.FindFirstValue("terminalId");
            return Guid.TryParse(termClaim, out var id) ? id : null;
        }
    }

    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    public IReadOnlyList<string> Permissions =>
        User?.FindAll("permission").Select(c => c.Value).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool HasPermission(string permission)
    {
        if (Roles.Contains("SuperAdmin")) return true;
        return Permissions.Contains(permission);
    }
}
