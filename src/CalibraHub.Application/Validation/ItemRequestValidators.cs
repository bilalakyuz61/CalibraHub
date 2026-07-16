using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

public sealed class CreateItemRequestValidator : AbstractValidator<CreateItemRequest>
{
    public CreateItemRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Malzeme kodu zorunludur.")
            .MaximumLength(50);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Malzeme adi zorunludur.")
            .MaximumLength(200);

        RuleFor(x => x.TaxRate)
            .InclusiveBetween(0m, 100m).WithMessage("KDV orani 0-100 araliginda olmalidir.");

        RuleFor(x => x.Barcode)
            .MaximumLength(50).WithMessage("Barkod en fazla 50 karakter olabilir.");
    }
}

public sealed class UpdateItemRequestValidator : AbstractValidator<UpdateItemRequest>
{
    public UpdateItemRequestValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0).WithMessage("Malzeme ID zorunludur.");
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TaxRate).InclusiveBetween(0m, 100m);
        RuleFor(x => x.Barcode).MaximumLength(50).WithMessage("Barkod en fazla 50 karakter olabilir.");
    }
}
