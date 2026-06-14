using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir Integration'in tek hedef alani icin mapping kurali. Wizard Step 3'te her hedef
/// alan icin 1 satir uretilir. 4 kaynak tipi:
///   FormField — kaynak formdan bir alan
///   Constant  — sabit literal deger
///   Formula   — NCalc expression
///   Lookup    — standart rehber (cbv_Guide_*) cozumlemesi
/// </summary>
[Description("Bir entegrasyonun bir hedef alani icin mapping kurali (4 kaynak tipinden biri).")]
public sealed class IntegrationMapping
{
    public int Id { get; init; }

    /// <summary>FK -> Integration.Id. CASCADE DELETE.</summary>
    public int IntegrationId { get; set; }

    /// <summary>Hedef JSON yolu. Nested: "FatUst.CariKod". Array: "Kalemler[].StokKod".</summary>
    public required string TargetPath { get; set; }

    /// <summary>Hedef tip: string / decimal / datetime / int / bool. Format dönüşümü için.</summary>
    public string? TargetDataType { get; set; }

    public IntegrationSourceType SourceType { get; set; }

    /// <summary>Kaynak ne dedigine bagli icerik:
    ///   FormField → field code (orn. "MusteriKodu")
    ///   Constant  → literal (orn. "ftYurtIci")
    ///   Formula   → NCalc expr (orn. "Adet * BirimFiyat")
    ///   Lookup    → guide code (orn. "COUNTRIES")</summary>
    public string? SourceValue { get; set; }

    /// <summary>SourceType=Lookup ise: hangi form alaninin degeri rehbere verilecek.</summary>
    public string? LookupSourceField { get; set; }

    /// <summary>Kaynak null/bos ise kullanilacak default deger.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Format dönüşümü kalibi: "yyyy-MM-dd" | "N2" | "upper" | vb.</summary>
    public string? FormatPattern { get; set; }

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Nested mapping icin grup: "FatUst" | "Kalemler" — JSON output'a dogru iliskilendirme.</summary>
    public string? GroupKey { get; set; }

    /// <summary>
    /// Master-Detail entegrasyon — 3 katmanli "veri seti" katmanini belirtir:
    ///   "Header"      = ust form alani (default, geriye uyum)
    ///   "Lines"       = kalem form alani (her kalem satiri icin tekrar — Kalems[] gibi array hedefler)
    ///   "Combination" = DocumentLine.CombinationId → ItemConfiguration.RecordCode runtime resolver
    /// MappingEngine.BuildAsync bu degeri kullanip uygun veri kaynagindan deger ceker.
    /// </summary>
    public string SourceSection { get; set; } = "Header";

    /// <summary>
    /// SourceType=Lookup icin coklu WHERE filtreleri (opsiyonel). JSON array:
    ///   [
    ///     { "field": "CARI_TIP",  "operator": "eq", "sourceField": "MusteriTipi" },
    ///     { "field": "IS_ACTIVE", "operator": "eq", "value": "1", "logic": "and" }
    ///   ]
    /// Engine runtime'da GuideConstraintDto[]'ya parse edip IGuideService.SearchAsync'e
    /// geçirir. sourceField → form data'dan deger cekilir; value → sabit deger.
    /// NULL/bos: tek anahtar lookup (LookupSourceField) — geriye uyum.
    /// </summary>
    public string? LookupFiltersJson { get; set; }

    /// <summary>
    /// SourceType=Function ve fonksiyon SqlFunctionName modunda ise: 3. parametre (@P3).
    /// Kullanici Wizard Step 3'te serbest yazar (orn. doviz kodu, depo kodu, parametre).
    /// Diger SourceType'lar veya View+Key fonksiyonlari icin ignore edilir.
    /// </summary>
    public string? LookupParam { get; set; }

    /// <summary>
    /// SourceType=Lookup icin hangi guide kolonu donulecek (opsiyonel).
    /// NULL ise guide'in DisplayColumn'i (genelde "Name") doner — geriye uyum.
    /// Set ise SearchAsync ile satir cekilip bu kolonun degeri alinir.
    /// Ornek: aynı CONTACTS guide'inden bir mapping CARI_AD ceker, baska mapping VERGI_NO.
    /// </summary>
    public string? LookupReturnColumn { get; set; }

    /// <summary>
    /// 2026-05-22 Cascade: Bu mapping satırı bir FK alanını hedefliyorsa, parent integration
    /// çalıştırılmadan ÖNCE FK'nin işaret ettiği entity'yi ERP'ye push'lamak için tetiklenecek
    /// cascade hedef integration'ın ID'si. NULL = cascade yok (default).
    ///
    /// Senaryo: Sipariş integration'ı "FatUst.CariKod ← ContactId" mapping satırında
    /// CascadeToIntegrationId="Netsis Cari Ekle (Id=5)" set edilir. Runner sipariş
    /// çalıştırırken bu satırı görür; ContactId değerini (örn. 123) okur, IntegrationRecordStatus
    /// kontrol eder, !Sent ise Integration ID=5'i recordId="123" ile cascade tetikler.
    ///
    /// Hedef integration sadece "Standalone" mantığını bilmek zorunda değil — aynı integration
    /// hem manuel/cron hem cascade ile çağrılabilir (tek tanım, çok yol).
    /// </summary>
    public int? CascadeToIntegrationId { get; set; }
}
