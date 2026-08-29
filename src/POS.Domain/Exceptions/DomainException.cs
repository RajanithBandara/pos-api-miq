using System;

namespace POS.Domain.Exceptions;

public class DomainException : Exception
{
    public string? ErrorCode { get; }

    public DomainException(string message, string? errorCode = null) : base(message)
    {
        ErrorCode = errorCode;
    }

    public DomainException(string message, Exception innerException, string? errorCode = null) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public class EntityNotFoundException : DomainException
{
    public string EntityName { get; }
    public object EntityKey { get; }

    public EntityNotFoundException(string entityName, object entityKey)
        : base($"{entityName} with identifier '{entityKey}' was not found.", "ENTITY_NOT_FOUND")
    {
        EntityName = entityName;
        EntityKey = entityKey;
    }
}

public class InsufficientStockException : DomainException
{
    public Guid ProductId { get; }
    public decimal RequestedQuantity { get; }
    public decimal AvailableQuantity { get; }

    public InsufficientStockException(Guid productId, decimal requested, decimal available)
        : base($"Insufficient stock for product '{productId}'. Requested: {requested}, Available: {available}.", "INSUFFICIENT_STOCK")
    {
        ProductId = productId;
        RequestedQuantity = requested;
        AvailableQuantity = available;
    }
}

public class DuplicateEntityException : DomainException
{
    public string EntityName { get; }
    public string KeyName { get; }
    public object KeyValue { get; }

    public DuplicateEntityException(string entityName, string keyName, object keyValue)
        : base($"{entityName} with {keyName} '{keyValue}' already exists.", "DUPLICATE_ENTITY")
    {
        EntityName = entityName;
        KeyName = keyName;
        KeyValue = keyValue;
    }
}

public class SyncConflictException : DomainException
{
    public string EntityType { get; }
    public Guid EntityId { get; }
    public long ServerVersion { get; }
    public long ClientVersion { get; }

    public SyncConflictException(string entityType, Guid entityId, long serverVersion, long clientVersion)
        : base($"Conflict detected for {entityType} '{entityId}'. Client version: {clientVersion}, Server version: {serverVersion}.", "SYNC_CONFLICT")
    {
        EntityType = entityType;
        EntityId = entityId;
        ServerVersion = serverVersion;
        ClientVersion = clientVersion;
    }
}

public class ValidationDomainException : DomainException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationDomainException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation failures occurred.", "VALIDATION_ERROR")
    {
        Errors = errors;
    }
}

public class UnauthorizedDomainException : DomainException
{
    public UnauthorizedDomainException(string message = "User is not authorized to perform this operation.")
        : base(message, "UNAUTHORIZED")
    {
    }
}
