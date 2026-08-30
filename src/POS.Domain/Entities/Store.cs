using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// A physical retail location. Every terminal, and therefore every synced event, belongs to
/// exactly one of these — it is the partition key the whole sync design rests on.
/// </summary>
public sealed class Store : BaseAuditableEntity<Guid>
{
    private Store() { }

    /// <summary>Short human handle used in reports and support, e.g. "MAIN" or "BR02".</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? ContactNumber { get; private set; }
    public string? TaxRegistrationNumber { get; private set; }

    /// <summary>
    /// IANA zone for this store's trading day. Reports group sales by local calendar day, so
    /// without this a shop in Colombo would have its takings split across two UTC dates.
    /// </summary>
    public string TimeZoneId { get; private set; } = "Asia/Colombo";

    public bool IsActive { get; private set; } = true;

    private readonly List<Terminal> _terminals = [];
    public IReadOnlyCollection<Terminal> Terminals => _terminals;

    public static Store Create(
        string code,
        string name,
        string? address = null,
        string? contactNumber = null,
        string? taxRegistrationNumber = null,
        string? timeZoneId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Store code is required.", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Store name is required.", nameof(name));

        return new Store
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Address = Normalise(address),
            ContactNumber = Normalise(contactNumber),
            TaxRegistrationNumber = Normalise(taxRegistrationNumber),
            TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? "Asia/Colombo" : timeZoneId.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Update(
        string name,
        string? address,
        string? contactNumber,
        string? taxRegistrationNumber,
        string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Store name is required.", nameof(name));

        Name = name.Trim();
        Address = Normalise(address);
        ContactNumber = Normalise(contactNumber);
        TaxRegistrationNumber = Normalise(taxRegistrationNumber);
        if (!string.IsNullOrWhiteSpace(timeZoneId)) TimeZoneId = timeZoneId.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
