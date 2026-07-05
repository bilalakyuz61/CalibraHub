using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

/// <summary>
/// SaveBOMRequest input dogrulamasi (rapor 2026-05-17 madde 3.11).
/// Auto-validation aktif (Program.cs: AddFluentValidationAutoValidation) — POST
/// /Logistics/SaveBOM her cagrida otomatik tetiklenir, ihlal 400 + ModelState donur.
///
/// Cross-aggregate dogrulamalar (mamul/bilesen aktif mi, dongusel bagimlilik var mi)
/// service tarafindadir; bu validator yalnizca request SEKLININ dogrulugunu kontrol eder.
/// </summary>
public sealed class SaveBOMRequestValidator : AbstractValidator<SaveBOMRequest>
{
    public SaveBOMRequestValidator()
    {
        // Mamul referansi — ItemId veya ParentMaterialCode'tan en az biri olmali.
        // Hatali mesaj eskiden "Mamul (ItemId veya ParentMaterialCode) belirtilmedi."
        // teknik tonluydu; kullanici dostu hale getirildi.
        RuleFor(x => x)
            .Must(r => r.ItemId > 0 || !string.IsNullOrWhiteSpace(r.ParentMaterialCode))
            .WithMessage("Mamul secmek zorunludur. Listeden bir mamul kodu seciniz.");

        // ImageRotation: yalnizca 0/90/180/270 — defansif (UI normalize ediyor, yine de input dogrulamasi)
        RuleFor(x => x.ImageRotation)
            .Must(r => r == 0 || r == 90 || r == 180 || r == 270)
            .WithMessage("Gorsel donus acisi 0, 90, 180 veya 270 derece olmalidir.");

        // ImageFitMode: dolu ise whitelist'ten olmali
        RuleFor(x => x.ImageFitMode)
            .Must(m => string.IsNullOrWhiteSpace(m)
                       || string.Equals(m, "square",  System.StringComparison.OrdinalIgnoreCase)
                       || string.Equals(m, "free",    System.StringComparison.OrdinalIgnoreCase)
                       || string.Equals(m, "contain", System.StringComparison.OrdinalIgnoreCase))
            .WithMessage("Gorsel oturma modu yalniz 'square', 'free' veya 'contain' olabilir.");

        // Lines koleksiyonu — null degil + en az 1 satir
        RuleFor(x => x.Lines)
            .NotNull().WithMessage("Recetede en az bir bilesen olmalidir.")
            .Must(l => l != null && l.Count > 0)
            .WithMessage("Recetede en az bir bilesen olmalidir.");

        // Her satira detayli validator
        RuleForEach(x => x.Lines).SetValidator(new SaveBOMLineRequestValidator());
    }
}

/// <summary>
/// BOM satir-bazli input dogrulamasi. Cross-aggregate kontroller (item aktif mi)
/// service tarafindadir; burada yalniz numerik invariant'lar (Quantity > 0,
/// ScrapRatio >= 0) ve referans bütünlüğü (ItemId veya kod en az birinde dolu).
/// </summary>
public sealed class SaveBOMLineRequestValidator : AbstractValidator<SaveBOMLineRequest>
{
    public SaveBOMLineRequestValidator()
    {
        RuleFor(x => x)
            .Must(l => l.ItemId > 0 || !string.IsNullOrWhiteSpace(l.ComponentMaterialCode))
            .WithMessage("Her bilesen icin bir malzeme secmek zorunludur.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0m)
            .WithMessage("Bilesen miktari sifirdan buyuk olmalidir.");

        RuleFor(x => x.ScrapRatio)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Fire orani negatif olamaz (0 veya daha buyuk olmalidir).");

        // 2026-07-05: satır açıklaması — DB kolonu NVARCHAR(1000)
        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .WithMessage("Bileşen açıklaması en fazla 1000 karakter olabilir.");
    }
}
