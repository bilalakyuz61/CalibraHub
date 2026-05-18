using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

/// <summary>
/// CreateDepartmentRequest validator (rapor §2.5 referans).
/// Service'ten manuel validation pattern'i artik bu sinifta toplaniyor.
/// </summary>
public sealed class CreateDepartmentRequestValidator : AbstractValidator<CreateDepartmentRequest>
{
    public CreateDepartmentRequestValidator()
    {
        RuleFor(x => x.CompanyId)
            .GreaterThan(0).WithMessage("Sirket secimi zorunludur.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Departman adi bos olamaz.")
            .MaximumLength(200).WithMessage("Departman adi en fazla 200 karakter olabilir.");
    }
}
