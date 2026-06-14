namespace CalibraHub.Domain.Entities;

/// <summary>
/// WidgetMas — Form widget tanimi (master).
///
/// EAV mimarinin katalog tablosu. Her satir bir widget (alan) tanimidir;
/// DataType='group' olan satirlar katlanir grup basliklaridir ve alt field'lar
/// ParentId ile bu gruplara baglanir.
///
/// FormId mevcut dbo.Forms tablosuna FK referans verir (dbo.Forms seed edilmis
/// form katalogudur: ITEMS, CONTACTS, SALES_QUOTE, vs.).
///
/// OptionsJson: sadece dropdown / multi-select tipleri icin dolu. Format:
/// basit liste ["Mavi","Yesil"] veya {"k":"key","l":"label"} ciftleri.
///
/// RulesJson (Faz G): kural ve formul motoru JSON objesi. Format:
/// {"visibleIf":"w_a == true","disabledIf":"w_b != 'x'","formula":"w_c * w_d"}.
/// Tum slotlar opsiyonel. Frontend expr-eval ile parse eder, React'te
/// dependency-driven incremental recomputation calistirir.
/// </summary>
public sealed class WidgetDefinition
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public int FormId { get; init; }
    public int? ParentId { get; init; }
    public required string WidgetCode { get; init; }
    public required string Label { get; init; }
    public required string DataType { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
    public int? ExpectedLength { get; init; }
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public int SortOrder { get; init; }
    public string? OptionsJson { get; init; }
    public string? RulesJson { get; init; }
    /// <summary>
    /// DEPRECATED — yerine <see cref="LabelStyle"/> = "inline" kullanin. Kolon DB'de
    /// korunuyor (eski okuyucular bozulmasin); renderer artik bu alana bakmaz,
    /// yalnizca LabelStyle uzerinden karar verir. Mevcut IsPlainField=1 satirlari
    /// kademeli olarak LabelStyle='inline' degerine migrate edilir
    /// (CalibraDatabaseInitializer.cs icindeki idempotent UPDATE).
    /// </summary>
    public bool IsPlainField { get; init; } = false;
    public bool IsRequired { get; init; } = false;
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Renk modu: 0 = Statik token (ColorValue direkt token kelimesi), 1 = Dinamik SQL
    /// (ColorValue bir alan adı; o alanın değeri çalışma zamanında token olarak okunur).
    /// </summary>
    public int ColorType { get; init; } = 0;
    /// <summary>
    /// Statik modda: 'slate' | 'blue' | 'emerald' | 'amber' | 'red' | 'indigo'.
    /// Dinamik modda: aynı formdaki başka bir widget'ın WidgetCode'u.
    /// Asla HEX kodu tutulmaz — sadece semantik token.
    /// </summary>
    public string? ColorValue { get; init; }
    /// <summary>
    /// Form uzerinde kaplayacagi 24-kolonlu grid span'i (1-24).
    /// Varsayilan 12 = 1/2 satir. Renderer CSS grid-column'a cevirir.
    /// Daha hassas genislik ayari icin 12 yerine 24 kolon secildi.
    /// </summary>
    public int ColSpan { get; init; } = 12;
    /// <summary>
    /// Etiket gorunum stili: "standard" (label input ustunde), "modern" (floating)
    /// veya "inline" (label gizli/sol-yaslanmis — eski IsPlainField davranisi).
    /// Otoriter alan: bu artik tek dogruluk kaynagidir; IsPlainField senkronize edilir.
    /// </summary>
    public string LabelStyle { get; init; } = "standard";

    /// <summary>
    /// "Standart alan" mi? (Universal Form Engine — Sprint 1).
    /// <para>
    /// true  → Bu widget Domain entity'sinin bir public property'sine baglidir
    ///         (orn. Item.Code). Save sirasinda deger WidgetValue/WidgetTra'ya degil,
    ///         entity tablosunun kendi kolonuna yazilir. <see cref="EntityColumn"/>
    ///         hangi property'ye baglandigini belirtir.
    /// </para>
    /// <para>
    /// false → Klasik custom widget. Deger EAV pattern'i ile WidgetTra'ya yazilir.
    /// </para>
    /// Discovery ile otomatik seed edilen "standart alan" satirlari true; admin'in
    /// UI'dan ekledigi ozel alanlar false.
    /// </summary>
    public bool IsSystemField { get; init; } = false;

    /// <summary>
    /// Bagli oldugu Domain entity property adi (case-sensitive, Pascal). Sadece
    /// IsSystemField=true ise dolu olur. Orn. "Code", "Name", "TaxNumber".
    /// </summary>
    public string? EntityColumn { get; init; }

    /// <summary>
    /// 2026-06-08 — Yetkilendirilebilir alan mı? <c>true</c> ise startup discovery sırasında
    /// <c>PermissionDef</c>'e <c>FIELD:&lt;WidgetCode&gt;</c> action koduyla bir izin satırı
    /// upsert edilir. Yetki Yönetimi ekranında ilgili formun altında "Görüntüle" yetkisi olarak
    /// listelenir; kullanıcıya veya departmana özel olarak verilip alınabilir.
    /// Form render zamanında widget bu izne göre filtrelenir (yetkisiz alanlar hiç dönmez).
    /// </summary>
    public bool IsPermissionControlled { get; init; } = false;

    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
