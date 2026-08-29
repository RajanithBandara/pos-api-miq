using System;
using System.Collections.Generic;
using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

public class Employee : BaseAuditableEntity<Guid>, ISoftDeletable
{
    public Guid StoreId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? RoleTitle { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public decimal? HourlyRate { get; set; }
    public DateTime? HiredAtUtc { get; set; }
    public DateTime? TerminatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    // Navigation
    public Store? Store { get; set; }
    public User? User { get; set; }
    public ICollection<Sale> SalesProcessed { get; set; } = new List<Sale>();

    public Employee()
    {
        Id = Guid.NewGuid();
    }
}
