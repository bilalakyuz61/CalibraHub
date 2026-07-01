namespace CalibraHub.Application.Contracts;

// PR 3: GuideCatalogItemDto kaldirildi — UI artik /api/guides/views uzerinden
// fiziksel SQL view listesini kullaniyor (GuideViewInfoDto). GuideMas catalog
// indirection'i gereksiz oldugu icin bu DTO'ya ihtiyac kalmadi.

/// <summary>
/// Tek rehberin metadatasi — React modal kolon header'lari icin.
/// Schema endpoint'i bunu doner.
/// </summary>
public sealed record GuideSchemaDto(
    string GuideCode,
    string GuideLabel,
    string ValueColumn,
    string DisplayColumn,
    IReadOnlyCollection<string> Columns,
    string? DefaultSortColumn,
    /// <summary>Kolon adı → SQL veri tipi (nvarchar, int, decimal, datetime2, bit vb.).
    /// Frontend Alan Ayarları modal'ında kolon yanında küçük chip olarak gösterir.</summary>
    IReadOnlyDictionary<string, string>? ColumnTypes = null);

/// <summary>
/// Infinite-scroll dostu arama sonucu. HasMore=true ise React bir sonraki
/// sayfayi cekebilir (sunucu pageSize+1 trick'i ile hesapliyor).
/// Columns: view'in tum kolonlari — response'a dahil ki React kolon header'larini
/// client-side schema fetch etmeden cizebilsin.
/// </summary>
public sealed record GuideSearchResultDto(
    IReadOnlyCollection<GuideRowDto> Rows,
    IReadOnlyCollection<string> Columns,
    int Page,
    int PageSize,
    bool HasMore);

/// <summary>
/// Tek bir view satirinin sunuma hazir hali. Value = WidgetTra'ya yazilacak
/// degerin ham string hali, Display = ekrandaki input'ta gozukecek etiket,
/// Cells = tum kolonlar dinamik bir sozluk olarak (React modal tablosunda
/// her kolon icin).
/// </summary>
public sealed record GuideRowDto(
    string Value,
    string Display,
    IReadOnlyDictionary<string, object?> Cells);

/// <summary>
/// Tek value'dan display'i cozmek icin. Sayfa yuklemede WidgetTra.Value
/// okunuyor, bu endpoint display'i (AccountTitle gibi) doner — input'a
/// basilacak etiket.
/// </summary>
public sealed record GuideResolveDto(string Value, string? Display, Dictionary<string, object?>? Cells = null);

/// <summary>
/// cbv_Guide_% pattern'ine uyan bir SQL View'ın özet bilgisi.
/// Admin form modalındaki "SQL View Kaynağı" dropdown'unu besler.
/// </summary>
public sealed record GuideViewInfoDto(
    string ViewName,
    string SchemaName,
    IReadOnlyCollection<string> Columns,
    bool IsStandard = false,
    string? Tags = null);

/// <summary>
/// Dinamik kisit — rehber aramasinda cascading filtre.
///
/// Iki kullanim modu var:
///   1) Yapisal (struct): Field/Operator/Value uclusu — backend parametrize SQL uretir
///      (SQL injection guvenli; column allowlist ile dogrulanir)
///   2) Raw SQL fragment: RawSql doluysa, dogrudan WHERE'a append edilir
///      (admin trusted; token resolution frontend'de yapildigi icin string-replace,
///      column allowlist YOK — admin'in sorumluluğu)
///
/// Field: view kolon adi (ValidateIdentifier ile kontrol edilir)
/// Operator: eq, neq, gt, lt, like, in (switch-case ile SQL'e cevirilir)
/// Value: filtre degeri (parametrize edilir — asla SQL'e inline yazilmaz)
/// Logic: "and" (varsayilan) veya "or" — kisitlar arasi birlestirme mantigi
/// RawSql: opsiyonel raw SQL fragment — set ise Field/Operator/Value yok sayilir
/// </summary>
public sealed record GuideConstraintDto(
    string? Field    = null,
    string? Operator = null,
    string? Value    = null,
    string? Logic    = "and",
    string? RawSql   = null);
// NOT: Field/Operator/Value nullable hale getirildi — frontend RawSql modunda
// `{ rawSql, logic }` gonderir (struct alanlar yok). Eski non-nullable hali
// System.Text.Json positional record'da bu alanlar default'suz oldugu icin
// JSON exception atiyordu; controller catch ile sessizce yutuyor, constraint
// hic uygulanmiyordu. Nullable + default null bu sorunu cozer; runtime'da
// zaten string.IsNullOrWhiteSpace kontrolleri null-safe.

// PR 3: UpsertGuideRequest kaldirildi — admin GuideMas CRUD UI gereksiz oldugu
// icin POST /api/guides endpoint'i de kaldirildi.
