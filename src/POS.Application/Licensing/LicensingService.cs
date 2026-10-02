using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Application.Common.Security;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;

namespace POS.Application.Licensing;

public interface ILicensingService
{
    Task<Result<ValidateProductKeyResponse>> ValidateKeyAsync(
        string productKey,
        CancellationToken cancellationToken = default);

    Task<Result<ActivateStoreLicenseResponse>> ActivateAsync(
        ActivateStoreLicenseRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class LicensingService(
    IProductKeyRepository keys,
    IStoreRepository stores,
    ITerminalRepository terminals,
    ISecretHasher hasher,
    IUnitOfWork unitOfWork,
    ILogger<LicensingService> logger) : ILicensingService
{
    public async Task<Result<ValidateProductKeyResponse>> ValidateKeyAsync(
        string productKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productKey))
            return Result<ValidateProductKeyResponse>.Failure("A product key is required.", "missing_key");

        var normalised = productKey.Trim().ToUpperInvariant();
        var key = await keys.FindByCodeAsync(normalised, cancellationToken);

        if (key is null)
        {
            logger.LogWarning("Product key validation failed: key '{Key}' not found.", normalised);
            return Result<ValidateProductKeyResponse>.Failure("Invalid product key. Please verify the characters and try again.", "invalid_key");
        }

        if (key.Status == ProductKeyStatus.Expired || (key.ExpiresAtUtc.HasValue && key.ExpiresAtUtc.Value <= DateTime.UtcNow))
        {
            logger.LogWarning("Product key '{Key}' is expired.", normalised);
            return Result<ValidateProductKeyResponse>.Failure("This product license key has expired.", "key_expired");
        }

        if (key.Status == ProductKeyStatus.Suspended)
        {
            logger.LogWarning("Product key '{Key}' is suspended.", normalised);
            return Result<ValidateProductKeyResponse>.Failure("This product license key has been suspended. Please contact support.", "key_suspended");
        }

        if (key.AssignedStoreId.HasValue)
        {
            var store = await stores.FindByIdAsync(key.AssignedStoreId.Value, cancellationToken);
            var storeTerminals = await terminals.GetByStoreAsync(key.AssignedStoreId.Value, cancellationToken);
            var activeTerminalsCount = storeTerminals.Count(t => t.Status != TerminalStatus.Revoked);

            return Result<ValidateProductKeyResponse>.Success(new ValidateProductKeyResponse(
                Valid: true,
                KeyCode: key.KeyCode,
                PlanTier: key.PlanTier,
                MaxTerminals: key.MaxTerminals,
                Status: key.Status.ToString(),
                IsStoreAssigned: true,
                StoreId: store?.Id,
                StoreName: store?.Name,
                StoreCode: store?.Code,
                EnrolledTerminalsCount: activeTerminalsCount,
                ExpiresAtUtc: key.ExpiresAtUtc));
        }

        return Result<ValidateProductKeyResponse>.Success(new ValidateProductKeyResponse(
            Valid: true,
            KeyCode: key.KeyCode,
            PlanTier: key.PlanTier,
            MaxTerminals: key.MaxTerminals,
            Status: key.Status.ToString(),
            IsStoreAssigned: false,
            StoreId: null,
            StoreName: null,
            StoreCode: null,
            EnrolledTerminalsCount: 0,
            ExpiresAtUtc: key.ExpiresAtUtc));
    }

    public async Task<Result<ActivateStoreLicenseResponse>> ActivateAsync(
        ActivateStoreLicenseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TerminalUid == Guid.Empty)
            return Result<ActivateStoreLicenseResponse>.Failure("A valid terminal identifier is required.", "missing_terminal_uid");

        if (string.IsNullOrWhiteSpace(request.ProductKey))
            return Result<ActivateStoreLicenseResponse>.Failure("A product key is required.", "missing_key");

        var normalisedKey = request.ProductKey.Trim().ToUpperInvariant();
        var key = await keys.FindByCodeAsync(normalisedKey, cancellationToken);

        if (key is null)
        {
            logger.LogWarning("Activation refused: product key '{Key}' not found.", normalisedKey);
            return Result<ActivateStoreLicenseResponse>.Failure("Invalid product key.", "invalid_key");
        }

        if (!key.IsUsable)
        {
            logger.LogWarning("Activation refused: product key '{Key}' has status {Status}.", normalisedKey, key.Status);
            return Result<ActivateStoreLicenseResponse>.Failure($"Product key is {key.Status}.", "key_not_usable");
        }

        var existingTerminal = await terminals.FindByUidAsync(request.TerminalUid, cancellationToken);

        if (existingTerminal is not null && key.AssignedStoreId.HasValue && existingTerminal.StoreId != key.AssignedStoreId.Value)
        {
            return Result<ActivateStoreLicenseResponse>.Failure(
                "This terminal is already enrolled to a different store.", "terminal_conflict");
        }

        if (existingTerminal is not null && existingTerminal.Status == TerminalStatus.Revoked)
        {
            return Result<ActivateStoreLicenseResponse>.Failure(
                "This terminal has been revoked and cannot be enrolled.", "terminal_revoked");
        }

        var apiKey = SecretGenerator.NewApiKey();
        var apiKeyHash = hasher.Hash(apiKey);

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            Store store;

            if (!key.AssignedStoreId.HasValue)
            {
                // Scenario 1: Initial Store Registration
                if (string.IsNullOrWhiteSpace(request.StoreName))
                    return Result<ActivateStoreLicenseResponse>.Failure("Store name is required for new store registration.", "missing_store_name");

                if (string.IsNullOrWhiteSpace(request.StoreCode))
                    return Result<ActivateStoreLicenseResponse>.Failure("Store code is required.", "missing_store_code");

                var storeCode = request.StoreCode.Trim().ToUpperInvariant();
                if (await stores.ExistsByCodeAsync(storeCode, ct))
                    return Result<ActivateStoreLicenseResponse>.Failure($"Store code '{storeCode}' is already taken. Please choose another code.", "duplicate_store_code");

                store = Store.Create(
                    storeCode,
                    request.StoreName.Trim(),
                    request.Address,
                    request.ContactNumber,
                    request.TaxRegistrationNumber);

                await stores.AddAsync(store, ct);
                key.AssignToStore(store.Id);
                logger.LogInformation("Store '{StoreName}' ({StoreCode}) created and assigned product key '{Key}'.", store.Name, store.Code, key.KeyCode);
            }
            else
            {
                // Scenario 2: Enrolling an additional terminal on an existing store
                var foundStore = await stores.FindByIdAsync(key.AssignedStoreId.Value, ct);
                if (foundStore is null || !foundStore.IsActive)
                    return Result<ActivateStoreLicenseResponse>.Failure("The store linked to this product key is inactive or not found.", "store_inactive");

                store = foundStore;

                var storeTerminals = await terminals.GetByStoreAsync(store.Id, ct);
                var activeTerminalsCount = storeTerminals.Count(t => t.Status != TerminalStatus.Revoked && t.TerminalUid != request.TerminalUid);

                if (activeTerminalsCount >= key.MaxTerminals)
                {
                    logger.LogWarning("Terminal limit reached for store {StoreId} (active: {Count}, max: {Max})", store.Id, activeTerminalsCount, key.MaxTerminals);
                    return Result<ActivateStoreLicenseResponse>.Failure(
                        $"License limit reached. Your {key.PlanTier} plan allows up to {key.MaxTerminals} register terminals.",
                        "terminal_limit_reached");
                }
            }

            Terminal terminal;
            if (existingTerminal is not null)
            {
                existingTerminal.RotateApiKey(apiKeyHash);
                existingTerminal.UpdateDescription(request.CounterNumber, request.CounterName, request.MachineName, request.AppVersion);
                terminal = existingTerminal;
                logger.LogInformation("Existing terminal {TerminalUid} rotated credential for store {StoreId}.", terminal.TerminalUid, store.Id);
            }
            else
            {
                terminal = Terminal.Enroll(
                    store.Id,
                    request.TerminalUid,
                    apiKeyHash,
                    request.CounterNumber,
                    request.CounterName,
                    request.MachineName,
                    request.AppVersion);

                await terminals.AddAsync(terminal, ct);
                logger.LogInformation("Terminal {TerminalUid} enrolled into store {StoreId} ({StoreCode}).", terminal.TerminalUid, store.Id, store.Code);
            }

            await unitOfWork.SaveChangesAsync(ct);

            return Result<ActivateStoreLicenseResponse>.Success(new ActivateStoreLicenseResponse(
                TerminalId: terminal.Id,
                TerminalUid: terminal.TerminalUid,
                StoreId: store.Id,
                StoreName: store.Name,
                StoreCode: store.Code,
                ApiKey: apiKey,
                PlanTier: key.PlanTier,
                MaxTerminals: key.MaxTerminals,
                EnrolledAtUtc: terminal.EnrolledAtUtc));
        }, cancellationToken);
    }
}
