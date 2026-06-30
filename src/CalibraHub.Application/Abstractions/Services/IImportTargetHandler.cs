using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// İçe aktarım motoruna Excel'den eşlenmiş satırlar (her satır targetKey→değer sözlüğü).
/// MatchKeyField upsert anahtarı; MappedKeys gösterim sırası içindir.
/// </summary>
public sealed record ImportRowSet(
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    IReadOnlyList<string> MappedKeys,
    string? MatchKeyField);

/// <summary>
/// Bir hedef entity için içe-aktarım handler'ı. Her entity (Cari, Stok, Fiyat, Reçete,
/// Cari İletişim) bir implementasyon sağlar; <c>ImportService</c> Excel ayrıştırma + kolon
/// eşlemeyi yapıp satırları handler'a verir, handler doğrulama + upsert + raporlamayı üstlenir.
/// </summary>
public interface IImportTargetHandler
{
    /// <summary>Benzersiz entity kodu — "CONTACT", "ITEM", "PRICELIST", "BOM", "CONTACT_PERSON".</summary>
    string Entity { get; }

    /// <summary>Kullanıcıya gösterilen ad — "Cari Hesap", "Stok Kartı"...</summary>
    string Label { get; }

    /// <summary>Bu entity için eşlenebilir hedef alan kataloğu.</summary>
    IReadOnlyList<ImportTargetFieldDto> GetFields();

    /// <summary>Satırları doğrula (kayıt YAZMAZ) — insert/update/error dağılımı + örnek satırlar.</summary>
    Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct);

    /// <summary>Geçerli satırları kaydet (insert/update) ve satır-bazlı sonuç döndür.</summary>
    Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct);

    /// <summary>
    /// Dinamik (DB lookup) izinli değerler — boş şablon açılır listesi + "Gecerli Degerler"
    /// sayfası için. Statik <see cref="ImportTargetFieldDto.AllowedValues"/> taşımayan ama
    /// sınırlı-değerli alanlar (örn. Cari İletişim "Unvan"). Anahtar = alan Key. Varsayılan boş.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetDynamicAllowedValuesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
            new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>
    /// Dinamik durumu (örn. form özel-alan/widget tanımları) yükler. Preview/Commit/katalog
    /// ÖNCESİ çağrılır; sonrasında <see cref="GetFields"/> dinamik alanları da içerebilir. Varsayılan no-op.
    /// </summary>
    Task PreloadAsync(CancellationToken ct) => Task.CompletedTask;
}
