using FluentValidation;
using POS.Application.Inventory.DTOs;
using POS.Domain.Enums;

namespace POS.Application.Inventory.Validators;

public class StockAdjustmentValidator : AbstractValidator<StockAdjustmentRequestDto>
{
    public StockAdjustmentValidator()
    {
        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.AdjustedQuantity).NotEqual(0).WithMessage("Adjusted quantity cannot be 0.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public class ReceiveStockValidator : AbstractValidator<ReceiveStockRequestDto>
{
    public ReceiveStockValidator()
    {
        RuleFor(x => x.StoreId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.BatchNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Received quantity must be greater than zero.");
    }
}
