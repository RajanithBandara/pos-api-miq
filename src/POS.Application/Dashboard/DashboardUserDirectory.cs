using Microsoft.Extensions.Logging;
using POS.Domain.Entities;
using POS.Domain.Interfaces;

namespace POS.Application.Dashboard;

/// <summary>
/// Decides whether a verified Neon Auth subject is anybody here.
///
/// This is the hinge of the dashboard's security. Neon Auth will happily issue a valid,
/// correctly-signed token to anyone who signs up, so a token proves identity and nothing else.
/// Access exists only where an administrator has created a grant, and this service is what turns
/// the one into the other. When it returns null the caller is authenticated but unauthorised,
/// which is a 403 — deliberately distinct from the 401 an invalid token earns.
/// </summary>
public interface IDashboardUserDirectory
{
    Task<DashboardIdentity?> ResolveAsync(NeonAuthPrincipal principal, CancellationToken cancellationToken = default);
}

public sealed class DashboardUserDirectory(
    IDashboardUserRepository users,
    IUnitOfWork unitOfWork,
    ILogger<DashboardUserDirectory> logger) : IDashboardUserDirectory
{
    /// <summary>
    /// How stale a last-seen time is allowed to get. Without this the directory would write a
    /// row on every single dashboard request, turning a read-only page refresh into database
    /// traffic. Five minutes is precise enough for "who has been using this lately".
    /// </summary>
    private static readonly TimeSpan SeenThrottle = TimeSpan.FromMinutes(5);

    public async Task<DashboardIdentity?> ResolveAsync(
        NeonAuthPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(principal.Subject))
            return null;

        // Neon has repudiated this account. Refuse before touching a grant, so a ban takes
        // effect even where a grant is still on file.
        if (principal.Banned)
        {
            logger.LogWarning("Dashboard access refused for {Subject}: the account is banned in Neon Auth.",
                principal.Subject);
            return null;
        }

        var user = await users.FindByAuthSubjectAsync(principal.Subject, cancellationToken)
                   ?? await TryClaimGrantAsync(principal, cancellationToken);

        if (user is null)
        {
            // The normal case for someone who signed up but was never granted anything. Logged
            // at information rather than warning: it is not an attack, it is an unprovisioned user.
            logger.LogInformation("Dashboard access refused for {Subject}: no grant exists for this account.",
                principal.Subject);
            return null;
        }

        if (!user.CanSignIn)
        {
            logger.LogWarning("Dashboard access refused for {Subject}: grant {UserId} is deactivated.",
                principal.Subject, user.Id);
            return null;
        }

        await TouchAsync(user, principal, cancellationToken);

        return new DashboardIdentity(
            user.Id,
            principal.Subject,
            user.Email,
            user.DisplayName,
            user.Role,
            user.StoreId);
    }

    /// <summary>
    /// Binds a grant that was issued to an address before its owner had ever signed in.
    ///
    /// The verified-email check is the whole security of this path. An address alone is a claim
    /// anyone can make: without the check, registering an unverified Neon Auth account using an
    /// administrator's address would inherit that administrator's grant on first sign-in. With
    /// it, the binding requires control of the mailbox, which is what the grant was addressed to
    /// in the first place.
    /// </summary>
    private async Task<DashboardUser?> TryClaimGrantAsync(
        NeonAuthPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(principal.Email))
            return null;

        if (!principal.EmailVerified)
        {
            logger.LogWarning(
                "Dashboard access refused for {Subject}: {Email} is not verified, so it cannot claim a grant.",
                principal.Subject, principal.Email);
            return null;
        }

        var grant = await users.FindByEmailAsync(principal.Email, cancellationToken);
        if (grant is null)
            return null;

        // The address matches a grant that already belongs to a different account. Refusing is
        // the only safe answer: reassigning would hand this subject whatever the original holder
        // was allowed, which is exactly the takeover the binding is meant to be permanent against.
        if (grant.AuthSubject is not null)
        {
            logger.LogWarning(
                "Dashboard access refused for {Subject}: {Email} is already bound to another Neon Auth account.",
                principal.Subject, principal.Email);
            return null;
        }

        grant.LinkAuthSubject(principal.Subject);
        grant.UpdateProfile(principal.Email, principal.Name);
        grant.MarkSeen();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Dashboard grant {UserId} bound to Neon Auth subject {Subject} ({Email}).",
            grant.Id, principal.Subject, principal.Email);

        return grant;
    }

    private async Task TouchAsync(DashboardUser user, NeonAuthPrincipal principal, CancellationToken cancellationToken)
    {
        var stale = user.LastSeenAtUtc is null || DateTime.UtcNow - user.LastSeenAtUtc.Value > SeenThrottle;
        var renamed = !string.IsNullOrWhiteSpace(principal.Name) && principal.Name != user.DisplayName;

        if (!stale && !renamed) return;

        user.MarkSeen();

        // The address is deliberately not refreshed from the token. The grant was issued to an
        // address, and letting a token rewrite it would let someone change what an administrator
        // authorised by editing their own profile.
        if (renamed) user.UpdateProfile(null, principal.Name);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
