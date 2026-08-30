using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// A short, single-use code that lets an installer bind one till to one store without ever
/// handling a long-lived secret. It is deliberately typable: whoever sets up the counter
/// reads it off a screen and keys it in.
/// </summary>
public sealed class TerminalEnrollmentCode : BaseEntity<Guid>
{
    private TerminalEnrollmentCode() { }

    /// <summary>Formatted "XXXX-XXXX" from an alphabet with no look-alike characters.</summary>
    public string Code { get; private set; } = string.Empty;

    public Guid StoreId { get; private set; }
    public Store? Store { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? UsedAtUtc { get; private set; }
    public Guid? UsedByTerminalId { get; private set; }

    public bool IsRevoked { get; private set; }

    public string? Note { get; private set; }

    public static TerminalEnrollmentCode Issue(Guid storeId, string code, TimeSpan validFor, string? note = null)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("Store id is required.", nameof(storeId));

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));

        if (validFor <= TimeSpan.Zero)
            throw new ArgumentException("An enrollment code must be valid for a positive duration.", nameof(validFor));

        var now = DateTime.UtcNow;

        return new TerminalEnrollmentCode
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            Code = code.Trim().ToUpperInvariant(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(validFor),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
    }

    /// <summary>
    /// Why this code cannot be used, or null when it can. Returning the reason rather than a
    /// bare bool keeps the "expired" and "already used" cases distinguishable in the logs,
    /// which is the difference between a confused installer and a suspected attack.
    /// </summary>
    public string? RejectionReason(DateTime nowUtc)
    {
        if (IsRevoked) return "This enrollment code has been revoked.";
        if (UsedAtUtc is not null) return "This enrollment code has already been used.";
        if (nowUtc >= ExpiresAtUtc) return "This enrollment code has expired.";
        return null;
    }

    public bool IsUsable(DateTime nowUtc) => RejectionReason(nowUtc) is null;

    public void Consume(Guid terminalId)
    {
        if (terminalId == Guid.Empty)
            throw new ArgumentException("Terminal id is required.", nameof(terminalId));

        UsedAtUtc = DateTime.UtcNow;
        UsedByTerminalId = terminalId;
    }

    public void Revoke() => IsRevoked = true;
}
