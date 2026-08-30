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
    Task<Result<EnrollmentCodeDto>> IssueEnrollmentCodeAsync(Guid storeId, IssueEnrollmentCodeRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EnrollmentCodeDto>>> GetEnrollmentCodesAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TerminalDto>> GetTerminalsAsync(Guid? storeId = null, CancellationToken cancellationToken = default);
    Task<Result<TerminalDto>> SetTerminalStatusAsync(Guid terminalId, string action, CancellationToken cancellationToken = default);
}

public sealed class ProvisioningService(
    IStoreRepository stores,
    ITerminalRepository terminals,
    IEnrollmentCodeRepository codes,
    IUnitOfWork unitOfWork,
    ILogger<ProvisioningService> logger) : IProvisioningService
{
    public const string CodeDuplicateStore = "duplicate_store_code";
    public const string CodeNotFound = "not_found";
    public const string CodeInvalidAction = "invalid_action";

    /// <summary>
    /// Long enough for someone to walk to the till and key it in, short enough that a code
    /// left on a screen stops working by itself.
    /// </summary>
    private const int DefaultValidForMinutes = 60;
    private const int MaxValidForMinutes = 60 * 24 * 7;

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

    public async Task<Result<EnrollmentCodeDto>> IssueEnrollmentCodeAsync(
        Guid storeId,
        IssueEnrollmentCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var store = await stores.FindByIdAsync(storeId, cancellationToken);
        if (store is null)
            return Result<EnrollmentCodeDto>.Failure("Store not found.", CodeNotFound);

        if (!store.IsActive)
            return Result<EnrollmentCodeDto>.Failure(
                "That store is not active, so it cannot take new terminals.", CodeInvalidAction);

        var minutes = request.ValidForMinutes ?? DefaultValidForMinutes;
        if (minutes < 1 || minutes > MaxValidForMinutes)
            return Result<EnrollmentCodeDto>.Failure(
                $"Validity must be between 1 and {MaxValidForMinutes} minutes.", CodeInvalidAction);

        // Codes are short enough to be typed, so collisions are possible rather than
        // theoretical. Retry on a taken code instead of relying on the unique index to throw.
        string candidate;
        var attempts = 0;
        do
        {
            candidate = SecretGenerator.NewEnrollmentCode();
            attempts++;
        }
        while (await codes.ExistsByCodeAsync(candidate, cancellationToken) && attempts < 10);

        if (attempts >= 10)
            return Result<EnrollmentCodeDto>.Failure(
                "Could not allocate an unused enrollment code. Try again.", CodeInvalidAction);

        var code = TerminalEnrollmentCode.Issue(store.Id, candidate, TimeSpan.FromMinutes(minutes), request.Note);

        await codes.AddAsync(code, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Enrollment code {CodeId} issued for store {StoreCode}, valid {Minutes}m",
            code.Id, store.Code, minutes);

        return Result<EnrollmentCodeDto>.Success(MapCode(code, store.Name));
    }

    public async Task<Result<IReadOnlyList<EnrollmentCodeDto>>> GetEnrollmentCodesAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var store = await stores.FindByIdAsync(storeId, cancellationToken);
        if (store is null)
            return Result<IReadOnlyList<EnrollmentCodeDto>>.Failure("Store not found.", CodeNotFound);

        var all = await codes.GetByStoreAsync(storeId, cancellationToken);
        IReadOnlyList<EnrollmentCodeDto> mapped = all.Select(c => MapCode(c, store.Name)).ToList();

        return Result<IReadOnlyList<EnrollmentCodeDto>>.Success(mapped);
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

    private static EnrollmentCodeDto MapCode(TerminalEnrollmentCode c, string storeName) => new(
        c.Id, c.Code, c.StoreId, storeName, c.CreatedAtUtc, c.ExpiresAtUtc,
        c.UsedAtUtc, c.UsedByTerminalId, c.IsRevoked, c.Note);

    private static TerminalDto MapTerminal(Terminal t) => new(
        t.Id, t.TerminalUid, t.StoreId, t.Store?.Name ?? string.Empty,
        t.CounterNumber, t.CounterName, t.MachineName, t.AppVersion,
        t.Status, t.EnrolledAtUtc, t.LastSeenAtUtc);
}
