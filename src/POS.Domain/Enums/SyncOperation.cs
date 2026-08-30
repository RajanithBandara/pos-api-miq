namespace POS.Domain.Enums;

/// <summary>
/// Mirrors the till's own operation kind. Kept as a separate enum on this side rather than
/// shared through a package: the two systems deploy independently, and a wire contract that
/// changes shape the moment one of them recompiles is not a contract.
/// </summary>
public enum SyncOperation
{
    /// <summary>The payload is the aggregate's full state; apply it over whatever is held.</summary>
    Upsert = 0,

    /// <summary>The aggregate no longer exists at the origin.</summary>
    Delete = 1
}
