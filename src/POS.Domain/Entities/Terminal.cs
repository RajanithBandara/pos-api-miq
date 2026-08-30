using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

/// <summary>
/// A till enrolled against a store. This is the API's record of a WPF installation, and the
/// thing sync events are attributed to.
/// </summary>
public sealed class Terminal : BaseAuditableEntity<Guid>
{
    private Terminal() { }

    public Guid StoreId { get; private set; }
    public Store? Store { get; private set; }

    /// <summary>
    /// The till's own identity, minted once by the WPF installation and never changed. This
    /// is the join between the two systems, so it is unique across the whole API rather than
    /// per store — a machine that turns up claiming a different store is a mistake worth
    /// catching, not an extra row.
    /// </summary>
    public Guid TerminalUid { get; private set; }

    /// <summary>Display fields mirrored from the till, kept only so support can identify it.</summary>
    public string CounterNumber { get; private set; } = "01";
    public string CounterName { get; private set; } = "Main Counter";
    public string? MachineName { get; private set; }
    public string? AppVersion { get; private set; }

    public TerminalStatus Status { get; private set; } = TerminalStatus.Active;

    /// <summary>
    /// BCrypt hash of the device secret handed out at enrollment. The secret itself is
    /// returned exactly once and never stored, so a database leak cannot be replayed as a
    /// terminal.
    /// </summary>
    public string ApiKeyHash { get; private set; } = string.Empty;

    public DateTime EnrolledAtUtc { get; private set; }

    /// <summary>Last time this till presented a valid credential. Drives the "is it alive" view.</summary>
    public DateTime? LastSeenAtUtc { get; private set; }

    public static Terminal Enroll(
        Guid storeId,
        Guid terminalUid,
        string apiKeyHash,
        string? counterNumber,
        string? counterName,
        string? machineName,
        string? appVersion)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("Store id is required.", nameof(storeId));

        if (terminalUid == Guid.Empty)
            throw new ArgumentException("Terminal uid is required.", nameof(terminalUid));

        if (string.IsNullOrWhiteSpace(apiKeyHash))
            throw new ArgumentException("Api key hash is required.", nameof(apiKeyHash));

        var now = DateTime.UtcNow;

        return new Terminal
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            TerminalUid = terminalUid,
            ApiKeyHash = apiKeyHash,
            CounterNumber = Clean(counterNumber, "01").ToUpperInvariant(),
            CounterName = Clean(counterName, "Main Counter"),
            MachineName = Normalise(machineName),
            AppVersion = Normalise(appVersion),
            Status = TerminalStatus.Active,
            EnrolledAtUtc = now,
            CreatedAtUtc = now
        };
    }

    /// <summary>
    /// Issues a fresh secret to a till that is enrolling again — a reinstall, or a machine
    /// rebuilt from an image. The terminal keeps its identity and its history; only the
    /// credential changes, which is what makes recovery possible without orphaning the
    /// events already filed under this terminal.
    /// </summary>
    public void RotateApiKey(string apiKeyHash)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHash))
            throw new ArgumentException("Api key hash is required.", nameof(apiKeyHash));

        if (Status == TerminalStatus.Revoked)
            throw new InvalidOperationException("This terminal has been revoked and cannot be re-enrolled.");

        ApiKeyHash = apiKeyHash;
        Status = TerminalStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateDescription(string? counterNumber, string? counterName, string? machineName, string? appVersion)
    {
        CounterNumber = Clean(counterNumber, CounterNumber).ToUpperInvariant();
        CounterName = Clean(counterName, CounterName);
        if (!string.IsNullOrWhiteSpace(machineName)) MachineName = machineName.Trim();
        if (!string.IsNullOrWhiteSpace(appVersion)) AppVersion = appVersion.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkSeen()
    {
        LastSeenAtUtc = DateTime.UtcNow;
    }

    public void Suspend()
    {
        if (Status == TerminalStatus.Revoked)
            throw new InvalidOperationException("A revoked terminal cannot be suspended.");

        Status = TerminalStatus.Suspended;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        if (Status == TerminalStatus.Revoked)
            throw new InvalidOperationException("A revoked terminal cannot be reactivated; enroll it again.");

        Status = TerminalStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Kills the credential permanently. The row is kept rather than deleted because the
    /// events this terminal already sent still point at it.
    /// </summary>
    public void Revoke()
    {
        Status = TerminalStatus.Revoked;
        ApiKeyHash = string.Empty;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>True when this terminal is allowed to exchange its secret for a token.</summary>
    public bool CanAuthenticate => Status == TerminalStatus.Active && !string.IsNullOrEmpty(ApiKeyHash);

    private static string Clean(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
