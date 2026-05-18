using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

public sealed class SaveSmtpProfileRequestValidator : AbstractValidator<SaveSmtpProfileRequest>
{
    public SaveSmtpProfileRequestValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0).WithMessage("Sirket secimi zorunludur.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Host).NotEmpty().WithMessage("SMTP host adresi zorunludur.").MaximumLength(200);
        RuleFor(x => x.Port).InclusiveBetween(1, 65535).WithMessage("Port 1-65535 araliginda olmali.");

        When(x => !string.IsNullOrWhiteSpace(x.FromEmail), () =>
        {
            RuleFor(x => x.FromEmail).EmailAddress().WithMessage("Gonderici e-posta gecerli olmali.");
        });
    }
}

public sealed class SaveErpConnectionSettingsRequestValidator : AbstractValidator<SaveErpConnectionSettingsRequest>
{
    public SaveErpConnectionSettingsRequestValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0).WithMessage("Sirket secimi zorunludur.");
        RuleFor(x => x.Company).NotEmpty().WithMessage("Sirket adi zorunludur.").MaximumLength(50);
        RuleFor(x => x.Business).NotEmpty().WithMessage("Isletme zorunludur.").MaximumLength(50);
        RuleFor(x => x.Branch).NotEmpty().WithMessage("Sube zorunludur.").MaximumLength(50);
        RuleFor(x => x.Username).NotEmpty().WithMessage("Kullanici adi zorunludur.").MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Sifre zorunludur.");
    }
}
