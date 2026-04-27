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
    /// Form uzerinde kaplayacagi 12-kolonlu grid span'i (1-12).
    /// Varsayilan 6 = 1/2 satir. Renderer CSS grid-column'a cevirir.
    /// </summary>
    public int ColSpan { get; init; } = 6;
    /// <summary>
    /// Etiket gorunum stili: "standard" (label input ustunde) veya
    /// "modern" (floating/outlined — label input cercevesi uzerinde).
    /// Varsayilan "standard". Enum yerine string — genisletilebilir.
    /// </summary>
    public string LabelStyle { get; init; } = "standard";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
