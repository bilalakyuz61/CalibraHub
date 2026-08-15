using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// İçe aktarım motoruna eşlenmiş satırlar (her satır targetKey→değer sözlüğü).
/// <paramref name="MatchKeyFields"/> upsert anahtarıdır — BİRDEN FAZLA alan verilebilir
/// (bileşik anahtar, örn. Cari Kodu + Ad Soyad). Boş liste = eşleşme yok, hep insert.
/// MappedKeys gösterim sırası içindir.
/// </summary>
public sealed record ImportRowSet(
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    IReadOnlyList<string> MappedKeys,
    IReadOnlyList<string> MatchKeyFields,
    /// <summary>Eşleşen mevcut kayıt GÜNCELLENSİN mi? false ise satır atlanır.</summary>
    bool UpdateExisting = true,
    /// <summary>Eşleşmeyen satır için YENİ kayıt açılsın mı? false ise satır atlanır.</summary>
    bool InsertNew = true)
{
    /// <summary>Tek anahtarlı çağrı kolaylığı (Excel şablonları ve eski kod yolu).</summary>
    public ImportRowSet(
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        IReadOnlyList<string> mappedKeys,
        string? matchKeyField)
        : this(rows, mappedKeys,
               string.IsNullOrWhiteSpace(matchKeyField)
                   ? Array.Empty<string>()
                   : new[] { matchKeyField.Trim() })
    { }

    /// <summary>Geriye uyum — ilk anahtar. Yeni kod <see cref="MatchKeyFields"/> kullanmalı.</summary>
    public string? MatchKeyField => MatchKeyFields.Count > 0 ? MatchKeyFields[0] : null;
}

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

    /// <summary>
    /// Handler eşleşme anahtarına göre GÜNCELLEME yapabiliyor mu? false ise her satır
    /// DAİMA yeni kayıt açar — <c>MatchKeyField</c> yok sayılır.
    ///
    /// Excel'de (tek seferlik, elle tetiklenen) bu kabul edilebilir; veritabanı aktarımı
    /// cron ile tekrar tekrar çalıştığı için aynı iş her turda MÜKERRER kayıt üretir.
    /// Bu bayrak yanlış olursa kullanıcı zorunlu-anahtar kuralının koruduğunu sanır ama
    /// korumaz — bu yüzden varsayılan true DEĞİL, her handler bilinçli belirtmelidir.
    /// Varsayılan true bırakıldı ki mevcut upsert'li handler'lar davranış değiştirmesin;
    /// insert-only olanlar açıkça false döner.
    /// </summary>
    bool SupportsUpsert => true;

    /// <summary>
    /// Handler "kaynakta olmayan kayıtları pasife al" politikasını uygulayabiliyor mu?
    /// Gerektirdikleri: IsActive kavramı + tüm kayıtları listeleyebilme + pasife alabilme.
    /// </summary>
    bool SupportsDeactivate => false;

    /// <summary>
    /// Kaynakta BULUNMAYAN aktif kayıtları pasife alır ve sayısını döner.
    /// Karşılaştırma <see cref="ImportRowSet.MatchKeyFields"/> imzası üzerinden yapılır —
    /// handler kendi kayıtlarının o alanlardaki değerini bildiği için diff'i o hesaplar.
    ///
    /// <paramref name="previewOnly"/> true ise HİÇBİR ŞEY YAZILMAZ, yalnız sayı döner
    /// (kullanıcı aktarımdan önce kaç kaydın etkileneceğini görsün).
    /// Varsayılan: desteklemeyen handler 0 döner.
    /// </summary>
    Task<int> DeactivateAbsentAsync(ImportRowSet set, bool previewOnly, CancellationToken ct)
        => Task.FromResult(0);

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
