using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

public sealed class SaveDocumentRequestValidator : AbstractValidator<SaveDocumentRequest>
{
    public SaveDocumentRequestValidator()
    {
        RuleFor(x => x.DocumentDate)
            .NotEmpty().WithMessage("Belge tarihi zorunludur.")
            .LessThanOrEqualTo(DateTime.Now.AddYears(2))
            .GreaterThanOrEqualTo(new DateTime(2000, 1, 1))
            .WithMessage("Belge tarihi makul bir araliga olmalidir.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Para birimi zorunludur.")
            .Length(3).WithMessage("Para birimi 3 karakter olmali (TRY/USD/EUR).");

        RuleFor(x => x.DiscountRate)
            .InclusiveBetween(0m, 100m).WithMessage("Indirim orani 0-100 araliginda olmalidir.");

        RuleFor(x => x.TaxRate)
            .InclusiveBetween(0m, 100m).WithMessage("KDV orani 0-100 araliginda olmalidir.");

        RuleFor(x => x.Lines)
            .NotNull().Must(l => l.Count > 0).WithMessage("En az bir kalem olmalidir.");

        RuleForEach(x => x.Lines).SetValidator(new SaveDocumentLineRequestValidator());
    }
}

public sealed class SaveDocumentLineRequestValidator : AbstractValidator<SaveDocumentLineRequest>
{
    public SaveDocumentLineRequestValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0).WithMessage("Malzeme secimi zorunludur.");
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Miktar pozitif olmalidir.");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0m).WithMessage("Birim fiyat negatif olamaz.");
        RuleFor(x => x.DiscountRate)
            .InclusiveBetween(0m, 100m).WithMessage("Satir indirim orani 0-100 araliginda olmalidir.");

        // TrackCombinations true ise CombinationId zorunlu
        When(x => x.TrackCombinations == true, () =>
        {
            RuleFor(x => x.CombinationId).NotNull().GreaterThan(0)
                .WithMessage("Kombinasyon takipli urunler icin kombinasyon secimi zorunludur.");
        });
    }
}
