using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;

namespace POS.Application.Dashboard;

/// <summary>
/// Grants and withdraws dashboard access. Reached two ways on purpose: by an administrator
/// holding an admin grant, and — for the first grant of all, when no administrator exists yet —
/// by the out-of-band provisioning key. Both go through here so the rules are the same whoever
/// is asking.
/// </summary>
public interface IDashboardAccessService
{
    Task<Result<DashboardUserDto>> GrantAsync(
        GrantDashboardAccessRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardUserDto>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<Result<DashboardUserDto>> SetActiveAsync(
        Guid userId, bool isActive, CancellationToken cancellationToken = default);
}

public sealed class DashboardAccessService(
    IDashboardUserRepository users,
    IStoreRepository stores,
    IUnitOfWork unitOfWork,
    ILogger<DashboardAccessService> logger) : IDashboardAccessService
{
    public const string CodeDuplicateEmail = "duplicate_email";
    public const string CodeStoreNotFound = "store_not_found";
    public const string CodeInvalidEmail = "invalid_email";
    public const string CodeNotFound = "not_found";

    public async Task<Result<DashboardUserDto>> GrantAsync(
        GrantDashboardAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return Result<DashboardUserDto>.Failure("A valid email address is required.", CodeInvalidEmail);

        if (!Enum.IsDefined(request.Role))
            return Result<DashboardUserDto>.Failure($"'{request.Role}' is not a dashboard role.", CodeInvalidEmail);

        var email = request.Email.Trim().ToLowerInvariant();

        if (await users.FindByEmailAsync(email, cancellationToken) is not null)
            return Result<DashboardUserDto>.Failure(
                "That address already has dashboard access.", CodeDuplicateEmail);

        // A grant naming a store that does not exist would silently scope its holder to nothing,
        // which reads as a permissions bug long after the typo that caused it.
        Store? store = null;
        if (request.StoreId is Guid storeId && storeId != Guid.Empty)
        {
            store = await stores.FindByIdAsync(storeId, cancellationToken);
            if (store is null)
                return Result<DashboardUserDto>.Failure("That store does not exist.", CodeStoreNotFound);
        }

        var user = DashboardUser.Grant(email, request.DisplayName, request.Role, store?.Id);

        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Dashboard access granted to {Email} as {Role}, scoped to {Scope}.",
            email, request.Role, store?.Code ?? "every store");

        return Result<DashboardUserDto>.Success(ToDto(user, store?.Name));
    }

    public async Task<IReadOnlyList<DashboardUserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var all = await users.GetAllAsync(cancellationToken);
        return all.Select(u => ToDto(u, u.Store?.Name)).ToList();
    }

    public async Task<Result<DashboardUserDto>> SetActiveAsync(
        Guid userId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result<DashboardUserDto>.Failure("That dashboard user does not exist.", CodeNotFound);

        user.SetActive(isActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Dashboard access for {Email} was {State}.",
            user.Email, isActive ? "restored" : "withdrawn");

        return Result<DashboardUserDto>.Success(ToDto(user, user.Store?.Name));
    }

    /// <summary>
    /// Note what is absent: the Neon Auth subject is never returned. It identifies an account in
    /// an external system and listing it here would leak it to every administrator screen for no
    /// operational benefit — <see cref="DashboardUserDto.IsLinked"/> answers the only question
    /// anyone actually asks of it.
    /// </summary>
    private static DashboardUserDto ToDto(DashboardUser user, string? storeName) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.StoreId,
        storeName,
        user.IsActive,
        user.AuthSubject is not null,
        user.CreatedAtUtc,
        user.LastSeenAtUtc);
}
