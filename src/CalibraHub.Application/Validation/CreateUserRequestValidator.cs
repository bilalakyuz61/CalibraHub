using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0).WithMessage("Sirket secimi zorunludur.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad Soyad zorunludur.")
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta zorunludur.")
            .EmailAddress().WithMessage("Gecerli bir e-posta adresi giriniz.");

        RuleFor(x => x.EmployeeCode)
            .NotEmpty().WithMessage("Sicil kodu zorunludur.")
            .MaximumLength(50);

        RuleFor(x => x.DepartmentId)
            .NotNull().GreaterThan(0)
            .WithMessage("Departman secimi zorunludur.");

        RuleFor(x => x.Role).IsInEnum().WithMessage("Gecerli bir rol seciniz.");

        RuleForEach(x => x.Permissions).IsInEnum().WithMessage("Secilen yetkilerden biri gecersiz.");
    }
}
