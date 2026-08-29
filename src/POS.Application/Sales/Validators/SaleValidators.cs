using FluentValidation;
using POS.Application.Sales.DTOs;

namespace POS.Application.Sales.Validators;

public class CreateSaleRequestValidator : AbstractValidator<CreateSaleRequestDto>
{
    public CreateSaleRequestValidator()
    {
        RuleFor(x => x.StoreId).NotEmpty().WithMessage("StoreId is required.");
        RuleFor(x => x.PosTerminalId).NotEmpty().WithMessage("PosTerminalId is required.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Sale must contain at least one item.");
        RuleForEach(x => x.Items).SetValidator(new CreateSaleItemValidator());
        RuleFor(x => x.Payments).NotEmpty().WithMessage("Sale must contain at least one payment.");
        RuleForEach(x => x.Payments).SetValidator(new CreatePaymentValidator());
    }
}

public class CreateSaleItemValidator : AbstractValidator<CreateSaleItemRequestDto>
{
    public CreateSaleItemValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("ProductId is required.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("UnitPrice cannot be negative.");
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0).WithMessage("Discount cannot be negative.");
    }
}

public class CreatePaymentValidator : AbstractValidator<CreatePaymentRequestDto>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.Method).IsInEnum().WithMessage("Invalid payment method.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Payment amount must be greater than zero.");
        RuleFor(x => x.Currency).NotEmpty().Length(3).WithMessage("Currency must be a 3-letter ISO code.");
    }
}
