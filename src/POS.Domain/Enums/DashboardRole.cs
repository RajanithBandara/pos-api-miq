namespace POS.Domain.Enums;

/// <summary>
/// What a dashboard user may do. Deliberately a small ladder rather than a permission matrix:
/// the roles that exist are the ones the web dashboard actually distinguishes, and an
/// unused permission is a permission nobody checks correctly.
///
/// This lives in our database, never in the Neon Auth token. Neon issues a <c>role</c> claim
/// of "authenticated", which is a Postgres RLS role and says nothing about what the holder is
/// allowed to do here.
/// </summary>
public enum DashboardRole
{
    /// <summary>Read-only access to whatever store scope the grant allows.</summary>
    Viewer = 1,

    /// <summary>Everything a viewer can do. Reserved for store-level operational actions.</summary>
    Manager = 2,

    /// <summary>Store and terminal administration, and granting dashboard access to others.</summary>
    Admin = 3
}
