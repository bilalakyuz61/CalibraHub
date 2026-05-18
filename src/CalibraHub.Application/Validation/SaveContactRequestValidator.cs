using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

public sealed class SaveContactRequestValidator : AbstractValidator<SaveContactRequest>
{
    public SaveContactRequestValidator()
    {
        RuleFor(x => x.AccountCode)
            .NotEmpty().WithMessage("Cari kodu zorunludur.")
            .MaximumLength(50);

        RuleFor(x => x.AccountTitle)
            .NotEmpty().WithMessage("Cari unvani zorunludur.")
            .MaximumLength(200);

        RuleFor(x => x.AccountType)
            .InclusiveBetween((byte)1, (byte)3)
            .WithMessage("Cari tipi gecerli olmali (1=Musteri, 2=Satici, 3=Her ikisi).");

        // Email girilirse formatli olmali (opsiyonel ama dolduysa dogru)
        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email!).EmailAddress().WithMessage("Gecerli bir e-posta adresi giriniz.");
        });

        // VKN/TCKN — kurumsal cari VKN, bireysel TCKN
        When(x => !string.IsNullOrWhiteSpace(x.TaxNumber), () =>
        {
            RuleFor(x => x.TaxNumber!)
                .Matches(@"^\d{10}$").WithMessage("Vergi numarasi 10 hane olmalidir.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.IdentityNumber), () =>
        {
            RuleFor(x => x.IdentityNumber!)
                .Matches(@"^\d{11}$").WithMessage("TC kimlik numarasi 11 hane olmalidir.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.WaPhone), () =>
        {
            RuleFor(x => x.WaPhone!)
                .MinimumLength(10)
                .WithMessage("WhatsApp numarasi en az 10 hane olmalidir (ulke kodu dahil).");
        });
    }
}
