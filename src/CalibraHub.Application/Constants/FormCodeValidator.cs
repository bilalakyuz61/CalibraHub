using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Constants;

/// <summary>
/// Startup'ta FormCodes sabitleri ile dbo.Forms tablosunu karşılaştırır.
///
/// Amaç: <see cref="FormCodes"/> içindeki her sabitin DB'de aktif bir karşılığı
/// olduğunu doğrula. Eşleşmeyen varsa WARNING log üretilir — uygulama başlamaya
/// devam eder (production'da hard-fail istemiyoruz), ama ilk deploy'da hata yüzeye çıkar.
///
/// Çağrı: Program.cs'te DB initializer çalıştıktan SONRA, tek seferlik.
/// </summary>
public sealed class FormCodeValidator
{
    private readonly IWidgetRepository _widgetRepo;
    private readonly ILogger<FormCodeValidator> _logger;

    public FormCodeValidator(IWidgetRepository widgetRepo, ILogger<FormCodeValidator> logger)
    {
        _widgetRepo = widgetRepo;
        _logger     = logger;
    }

    /// <summary>
    /// FormCodes sabitleri ile DB'deki aktif formları karşılaştırır.
    /// </summary>
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        try
        {
            var dbForms = await _widgetRepo.GetFormsAsync(ct);
            var dbCodes = dbForms.Select(f => f.FormCode.ToUpperInvariant()).ToHashSet();

            var missingInDb    = new List<string>();
            var missingInConst = new List<string>();

            // FormCodes sabitlerinde olup DB'de olmayan → potential typo veya silinmiş form
            foreach (var code in FormCodes.All)
            {
                if (!dbCodes.Contains(code.ToUpperInvariant()))
                    missingInDb.Add(code);
            }

            if (missingInDb.Count > 0)
            {
                _logger.LogWarning(
                    "[FormCodeValidator] {Count} FormCode sabiti DB'de YOK (typo veya silinmiş form): {Codes}",
                    missingInDb.Count,
                    string.Join(", ", missingInDb));
            }

            // DB'de aktif olup FormCodes sabitlerinde olmayan → yeni ekran eklendi, sabit yazılmadı
            foreach (var code in dbCodes)
            {
                if (!FormCodes.All.Contains(code))
                    missingInConst.Add(code);
            }

            if (missingInConst.Count > 0)
            {
                _logger.LogWarning(
                    "[FormCodeValidator] {Count} DB FormCode'u FormCodes sabitlerinde YOK (FormCodes.cs güncellenmeli): {Codes}",
                    missingInConst.Count,
                    string.Join(", ", missingInConst));
            }

            if (missingInDb.Count == 0 && missingInConst.Count == 0)
            {
                _logger.LogInformation(
                    "[FormCodeValidator] Tüm {Count} FormCode sabiti DB ile eşleşti ✓", FormCodes.All.Count);
            }
        }
        catch (Exception ex)
        {
            // Validator hatası uygulamayı durdurmamalı
            _logger.LogWarning(ex, "[FormCodeValidator] Doğrulama sırasında hata — atlandı");
        }
    }
}
