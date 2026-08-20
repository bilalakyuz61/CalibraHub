namespace CalibraHub.Domain.Entities;

/// <summary>
/// FormFieldBehavior — standart alan davranış tanımı (Form Davranış Katmanı, 2026-08-05).
///
/// Ekran markup'ı SABİT kalır; bu tablo yalnızca davranış metadata'sı taşır:
/// Görünür / Zorunlu / Varsayılan Değer / Başlık Metni+Stili / koşullu kurallar
/// (RulesJson: {"visibleIf":"...","requiredIf":"..."} — widget RulesJSON sözlüğüyle
/// aynı format, RuleExpr.Sanitize süzgecinden geçer).
///
/// FieldKey: alan kataloğu key'i (örn. "paymentTerms") veya sekme pseudo-key'i
/// ("tab:conditions"). Katalogda olmayan key runtime'da YOK SAYILIR (additive-safe);
/// davranış satırı olmayan alan varsayılanla (görünür, zorunlu değil) çalışır —
/// fail-open: tablo boşken ekran bugünkü davranışla birebir aynıdır.
///
/// Saklama modeli: form başına full-replace (varsayılandan farklı davranışı olan
/// satırlar yazılır, diğerleri hiç tutulmaz) — FldSet'e bilinçli olarak DOKUNULMADI
/// (rehber eşleştirme akışları + PR4 refactor planı ile çakışmasın).
/// </summary>
public sealed class FormFieldBehavior
{
    public int Id { get; init; }
    public required string FormCode { get; init; }
    public required string FieldKey { get; init; }
    public bool IsVisible { get; set; } = true;
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? LabelText { get; set; }
    public string? LabelStyle { get; set; }
    public string? RulesJson { get; set; }
    /// <summary>
    /// Üst bilgi "kimlik + şerit" kart düzeni (2026-08-19): 0=Kimlik (kart başlığı),
    /// 1/2/3...=Şerit N. null = ayarlanmamış — varsayılan dağılım FRONTEND'de yorumlanır,
    /// backend yalnız taşır/saklar.
    /// </summary>
    public int? CardSection { get; set; }
    /// <summary>
    /// Kart düzeninde aynı CardSection içindeki görüntülenme sırası (2026-08-20):
    /// küçük önce. null = ayarlanmamış — varsayılan sıralama (katalog sırası)
    /// FRONTEND'de yorumlanır, backend yalnız taşır/saklar.
    /// </summary>
    public int? CardOrder { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? Updated { get; set; }
}
