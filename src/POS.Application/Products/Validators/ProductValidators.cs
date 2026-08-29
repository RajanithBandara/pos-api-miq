using FluentValidation;
using POS.Application.Products.DTOs;

namespace POS.Application.Products.Validators;

public class CreateProductValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50).WithMessage("SKU is required and must not exceed 50 characters.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Product name is required.");
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0).WithMessage("CostPrice cannot be negative.");
        RuleFor(x => x.RetailPrice).GreaterThanOrEqualTo(0).WithMessage("RetailPrice cannot be negative.");
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 1).WithMessage("TaxRate must be between 0 (0%) and 1 (100%).");
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
    }
}

public class UpdateProductValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RetailPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 1);
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
    }
}

public class CreateCategoryValidator : AbstractValidator<CreateCategoryDto>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
