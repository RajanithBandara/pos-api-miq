using POS.Domain.Common;

namespace POS.Domain.Entities;

public enum ProductKeyStatus
{
    Available = 0,
    Active = 1,
    Suspended = 2,
    Expired = 3
}

/// <summary>
/// Represents a commercial license key issued to a merchant for MarchIQ POS.
/// Keys follow the format: MIQ-{PLAN}-{RANDOM}-{RANDOM}-{RANDOM} (e.g. MIQ-PRO-7K4D-92LX-AB83).
/// </summary>
public sealed class ProductKey : BaseAuditableEntity<Guid>
{
    private ProductKey() { }

    /// <summary>The formatted product license key string, e.g. "MIQ-PRO-7K4D-92LX-AB83".</summary>
    public string KeyCode { get; private set; } = string.Empty;

    /// <summary>License plan tier: "PRO", "STD", "ENT".</summary>
    public string PlanTier { get; private set; } = "PRO";

    /// <summary>Maximum number of point-of-sale registers allowed to enroll under this store license.</summary>
    public int MaxTerminals { get; private set; } = 3;

    public ProductKeyStatus Status { get; private set; } = ProductKeyStatus.Available;

    /// <summary>The store this license is assigned to once activated. Nullable until first activation.</summary>
    public Guid? AssignedStoreId { get; private set; }
    public Store? AssignedStore { get; private set; }

    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }

    public static ProductKey Create(
        string keyCode,
        string planTier = "PRO",
        int maxTerminals = 3,
        DateTime? expiresAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(keyCode))
            throw new ArgumentException("Key code is required.", nameof(keyCode));

        return new ProductKey
        {
            Id = Guid.NewGuid(),
            KeyCode = keyCode.Trim().ToUpperInvariant(),
            PlanTier = string.IsNullOrWhiteSpace(planTier) ? "PRO" : planTier.Trim().ToUpperInvariant(),
            MaxTerminals = maxTerminals <= 0 ? 1 : maxTerminals,
            Status = ProductKeyStatus.Available,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void AssignToStore(Guid storeId)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("Store id is required.", nameof(storeId));

        if (Status == ProductKeyStatus.Expired)
            throw new InvalidOperationException("Cannot assign an expired license key.");

        if (Status == ProductKeyStatus.Suspended)
            throw new InvalidOperationException("Cannot assign a suspended license key.");

        AssignedStoreId = storeId;
        Status = ProductKeyStatus.Active;
        ActivatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetStatus(ProductKeyStatus status)
    {
        Status = status;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public bool IsUsable => Status is ProductKeyStatus.Available or ProductKeyStatus.Active &&
                            (ExpiresAtUtc is null || ExpiresAtUtc > DateTime.UtcNow);
}
