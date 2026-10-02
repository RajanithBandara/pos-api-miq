using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

/// <summary>
/// A person authorised to use the Angular dashboard. This is the API's own record of who may
/// see what — Neon Auth proves an identity, this decides what that identity is allowed to do.
///
/// The separation is the point. A Neon Auth token is proof that someone signed in to Neon Auth,
/// nothing more; anyone can sign up. Without a row here, a perfectly valid token grants no
/// access at all, which is what stops the API from authorising a request merely because it
/// carries a user id.
/// </summary>
public sealed class DashboardUser : BaseAuditableEntity<Guid>
{
    private DashboardUser() { }

    /// <summary>
    /// The Neon Auth subject (<c>sub</c>), bound on first sign-in and never changed afterwards.
    ///
    /// Null until then: access is granted by email before the person has ever signed in, because
    /// their subject cannot be known in advance. See <see cref="LinkAuthSubject"/> for why the
    /// binding is one-way.
    /// </summary>
    public string? AuthSubject { get; private set; }

    /// <summary>
    /// The address the grant was issued to. Used only to find an unbound grant on first
    /// sign-in; once <see cref="AuthSubject"/> is set, the subject is what identifies the user.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    public string? DisplayName { get; private set; }

    public DashboardRole Role { get; private set; } = DashboardRole.Viewer;

    /// <summary>
    /// The one store this user can see, or null for every store. This is the tenant boundary
    /// for the dashboard, and it is applied in the query services rather than trusted from a
    /// request parameter.
    /// </summary>
    public Guid? StoreId { get; private set; }
    public Store? Store { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTime? LastSeenAtUtc { get; private set; }

    /// <summary>
    /// Pre-authorises an address. The person may not have a Neon Auth account yet; they get
    /// access the first time they sign in with this address.
    /// </summary>
    public static DashboardUser Grant(string email, string? displayName, DashboardRole role, Guid? storeId)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("An email address is required.", nameof(email));

        if (!Enum.IsDefined(role))
            throw new ArgumentException($"'{role}' is not a dashboard role.", nameof(role));

        if (storeId == Guid.Empty)
            throw new ArgumentException("Use null for every store, not an empty id.", nameof(storeId));

        return new DashboardUser
        {
            Id = Guid.NewGuid(),
            Email = Normalise(email),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            Role = role,
            StoreId = storeId,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Binds this grant to a Neon Auth subject, permanently.
    ///
    /// One-way on purpose. If the binding could be reassigned, an address that changed hands —
    /// or an account that re-registered — would inherit whatever this grant allows. Rebinding
    /// requires revoking the grant and issuing a new one, which is a deliberate act by an
    /// administrator rather than a side effect of someone signing in.
    ///
    /// The caller must have established that the address is verified before calling this. See
    /// the directory service for why an unverified address must never bind a grant.
    /// </summary>
    public void LinkAuthSubject(string authSubject)
    {
        if (string.IsNullOrWhiteSpace(authSubject))
            throw new ArgumentException("An auth subject is required.", nameof(authSubject));

        if (AuthSubject is not null && AuthSubject != authSubject)
            throw new InvalidOperationException(
                "This dashboard user is already bound to a different Neon Auth subject.");

        AuthSubject = authSubject;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateProfile(string? email, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(email)) Email = Normalise(email);
        if (!string.IsNullOrWhiteSpace(displayName)) DisplayName = displayName.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ChangeAccess(DashboardRole role, Guid? storeId)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentException($"'{role}' is not a dashboard role.", nameof(role));

        Role = role;
        StoreId = storeId == Guid.Empty ? null : storeId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Withdraws or restores access. Deactivating takes effect on the next request rather than
    /// on the next token, because access is decided from this row on every call.
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkSeen()
    {
        LastSeenAtUtc = DateTime.UtcNow;
    }

    /// <summary>True when this grant may be used at all.</summary>
    public bool CanSignIn => IsActive;

    /// <summary>Whether this user is allowed to see the given store.</summary>
    public bool CanSee(Guid storeId) => StoreId is null || StoreId == storeId;

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
