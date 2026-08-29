using FluentValidation;
using POS.Application.Synchronization.DTOs;

namespace POS.Application.Synchronization.Validators;

public class SyncPushRequestValidator : AbstractValidator<SyncPushRequestDto>
{
    public SyncPushRequestValidator()
    {
        RuleFor(x => x.PosTerminalId).NotEmpty().WithMessage("PosTerminalId is required.");
        RuleFor(x => x.StoreId).NotEmpty().WithMessage("StoreId is required.");
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100).WithMessage("IdempotencyKey is required and must not exceed 100 chars.");
        RuleFor(x => x.SyncBatchId).NotEmpty().WithMessage("SyncBatchId is required.");
        RuleFor(x => x.Operations).NotNull().WithMessage("Operations list cannot be null.");
    }
}

public class SyncPullRequestValidator : AbstractValidator<SyncPullRequestDto>
{
    public SyncPullRequestValidator()
    {
        RuleFor(x => x.StoreId).NotEmpty().WithMessage("StoreId is required.");
        RuleFor(x => x.PosTerminalId).NotEmpty().WithMessage("PosTerminalId is required.");
        RuleFor(x => x.LastSyncVersion).GreaterThanOrEqualTo(0).WithMessage("LastSyncVersion must be >= 0.");
        RuleFor(x => x.BatchSize).InclusiveBetween(1, 1000).WithMessage("BatchSize must be between 1 and 1000.");
    }
}
