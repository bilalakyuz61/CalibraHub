using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

public sealed class UpdateDepartmentRequestValidator : AbstractValidator<UpdateDepartmentRequest>
{
    public UpdateDepartmentRequestValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Departman ID zorunludur.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Departman adi bos olamaz.")
            .MaximumLength(200);
    }
}
