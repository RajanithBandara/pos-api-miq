namespace POS.Domain.Enums;

/// <summary>
/// Whether a till is still allowed to talk to this API. Suspension and revocation are the
/// only levers that stop a terminal syncing, so they are recorded on the terminal itself
/// rather than inferred from the absence of a credential.
/// </summary>
public enum TerminalStatus
{
    /// <summary>Enrolled and permitted to push and pull.</summary>
    Active = 1,

    /// <summary>Temporarily blocked. The credential survives, so it can be re-activated.</summary>
    Suspended = 2,

    /// <summary>Permanently blocked, e.g. the machine was lost. The credential is dead.</summary>
    Revoked = 3
}
