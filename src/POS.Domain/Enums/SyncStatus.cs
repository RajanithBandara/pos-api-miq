namespace POS.Domain.Enums;

public enum SyncStatus
{
    Pending = 1,
    Success = 2,
    Failed = 3,
    Conflict = 4,
    IgnoredDuplicate = 5
}
