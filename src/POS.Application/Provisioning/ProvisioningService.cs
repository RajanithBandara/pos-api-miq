using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Common.Security;
using POS.Application.Terminals;
using POS.Domain.Entities;
using POS.Domain.Interfaces;

namespace POS.Application.Provisioning;

public interface IProvisioningService
{
    Task<Result<StoreDto>> CreateStoreAsync(CreateStoreRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreDto>> GetStoresAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TerminalDto>> GetTerminalsAsync(Guid? storeId = null, CancellationToken cancellationToken = default);
    Task<Result<TerminalDto>> SetTerminalStatusAsync(Guid terminalId, string action, CancellationToken cancellationToken = default);
}

public sealed class ProvisioningService(
    IStoreRepository stores,
    ITerminalRepository terminals,
    IUnitOfWork unitOfWork,
    ILogger<ProvisioningService> logger) : IProvisioningService
{
    public const string CodeDuplicateStore = "duplicate_store_code";
    public const string CodeNotFound = "not_found";
    public const string CodeInvalidAction = "invalid_action";

    public async Task<Result<StoreDto>> CreateStoreAsync(
        CreateStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Result<StoreDto>.Failure("A store code is required.", CodeDuplicateStore);

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<StoreDto>.Failure("A store name is required.", CodeDuplicateStore);

        var normalised = request.Code.Trim().ToUpperInvariant();
        if (await stores.ExistsByCodeAsync(normalised, cancellationToken))
            return Result<StoreDto>.Failure($"A store with code '{normalised}' already exists.", CodeDuplicateStore);

        if (!string.IsNullOrWhiteSpace(request.TimeZoneId) && !IsKnownTimeZone(request.TimeZoneId))
            return Result<StoreDto>.Failure(
                $"'{request.TimeZoneId}' is not a time zone this server recognises.", CodeInvalidAction);

        var store = Store.Create(
            normalised,
            request.Name,
            request.Address,
            request.ContactNumber,
            request.TaxRegistrationNumber,
            request.TimeZoneId);

        await stores.AddAsync(store, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Store {StoreCode} created as {StoreId}", store.Code, store.Id);

        return Result<StoreDto>.Success(MapStore(store, 0));
    }

    public async Task<IReadOnlyList<StoreDto>> GetStoresAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var all = await stores.GetAllAsync(includeInactive, cancellationToken);
        return all.Select(s => MapStore(s, s.Terminals.Count)).ToList();
    }

    public async Task<IReadOnlyList<TerminalDto>> GetTerminalsAsync(
        Guid? storeId = null,
        CancellationToken cancellationToken = default)
    {
        var all = storeId is Guid id
            ? await terminals.GetByStoreAsync(id, cancellationToken)
            : await terminals.GetAllAsync(cancellationToken);

        return all.Select(MapTerminal).ToList();
    }

    public async Task<Result<TerminalDto>> SetTerminalStatusAsync(
        Guid terminalId,
        string action,
        CancellationToken cancellationToken = default)
    {
        var terminal = await terminals.FindByIdAsync(terminalId, cancellationToken);
        if (terminal is null)
            return Result<TerminalDto>.Failure("Terminal not found.", CodeNotFound);

        try
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "suspend": terminal.Suspend(); break;
                case "reactivate": terminal.Reactivate(); break;
                case "revoke": terminal.Revoke(); break;
                default:
                    return Result<TerminalDto>.Failure(
                        $"'{action}' is not a terminal action. Use suspend, reactivate or revoke.", CodeInvalidAction);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result<TerminalDto>.Failure(ex.Message, CodeInvalidAction);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Terminal {TerminalId} {Action}d", terminalId, action.ToLowerInvariant());

        return Result<TerminalDto>.Success(MapTerminal(terminal));
    }

    private static bool IsKnownTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static StoreDto MapStore(Store s, int terminalCount) => new(
        s.Id, s.Code, s.Name, s.Address, s.ContactNumber, s.TaxRegistrationNumber,
        s.TimeZoneId, s.IsActive, terminalCount, s.CreatedAtUtc);

    private static TerminalDto MapTerminal(Terminal t) => new(
        t.Id, t.TerminalUid, t.StoreId, t.Store?.Name ?? string.Empty,
        t.CounterNumber, t.CounterName, t.MachineName, t.AppVersion,
        t.Status, t.EnrolledAtUtc, t.LastSeenAtUtc);
}
