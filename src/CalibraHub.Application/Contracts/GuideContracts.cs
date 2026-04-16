namespace CalibraHub.Application.Contracts;

/// <summary>
/// Admin UI icin rehber katalogu — GuideMas tablosundan tum kayitlar.
/// OptionsModal dropdown'unda listelenecek rehberler.
/// </summary>
public sealed record GuideCatalogItemDto(
    int Id,
    string GuideCode,
    string GuideLabel,
    string ViewName,
    string ValueColumn,
    string DisplayColumn,
    string? DefaultSortColumn,
    IReadOnlyCollection<string> Columns);

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
    string? DefaultSortColumn);

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
    IReadOnlyCollection<string> Columns);

/// <summary>
/// Yeni rehber eklemek veya mevcut rehberi güncellemek için istek nesnesi.
/// POST /api/guides
/// </summary>
/// <summary>
/// Dinamik kisit — rehber aramasinda cascading filtre.
/// Field: view kolon adi (ValidateIdentifier ile kontrol edilir)
/// Operator: eq, neq, gt, lt, like, in (switch-case ile SQL'e cevirilir)
/// Value: filtre degeri (parametrize edilir — asla SQL'e inline yazilmaz)
/// Logic: "and" (varsayilan) veya "or" — kisitlar arasi birlestirme mantigi
/// </summary>
public sealed record GuideConstraintDto(string Field, string Operator, string Value, string? Logic = "and");

public sealed record UpsertGuideRequest(
    int Id,                // 0 = yeni kayıt, >0 = güncelle
    string GuideLabel,
    string ViewName,
    string ValueColumn,
    string DisplayColumn,
    IReadOnlyCollection<string> GridColumns,
    string? DefaultSortColumn,
    string? GuideCode);    // null ise sistem otomatik üretir (GuideLabel'dan)
