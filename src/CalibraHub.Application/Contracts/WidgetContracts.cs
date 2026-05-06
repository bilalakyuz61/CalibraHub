namespace CalibraHub.Application.Contracts;

/// <summary>
/// WidgetContracts — EAV widget sisteminin DTO'lari.
///
/// "Aptal Bilesen, Zeki Veri" mimarisine hizmet eder:
///   - WidgetFormSchemaDto — bir formun tum widget tanimlarini icerir
///   - WidgetDefinitionDto — tek widget tanimi (admin UI icin)
///   - WidgetRenderDto     — schema + value birlesik DTO (edit sayfalari icin)
///   - SaveWidgetValuesRequest — kullanici input'unu save akisi icin
/// </summary>
public sealed record WidgetFormSchemaDto(
    int FormId,
    string FormCode,
    string FormLabel,
    IReadOnlyCollection<WidgetDefinitionDto> Widgets);

public sealed record WidgetDefinitionDto(
    int Id,
    int? ParentId,
    string WidgetCode,
    string Label,
    string DataType,
    int? MaxLength,
    int SortOrder,
    IReadOnlyCollection<string>? Options,
    bool IsActive,
    IReadOnlyDictionary<string, string>? Metadata = null,
    WidgetRulesDto? Rules = null,
    // DEPRECATED: yerine LabelStyle = "inline" kullanin (DB kolonu korunuyor).
    bool IsPlainField = false,
    bool IsRequired = false,
    int? MinLength = null,
    int? ExpectedLength = null,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    int ColorType = 0,
    string? ColorValue = null,
    int ColSpan = 12,
    string LabelStyle = "standard");

/// <summary>
/// Faz G — Kural ve formul motoru payload'i. Tum alanlar opsiyonel string ifade.
///   visibleIf  : bool sonuclu ifade — false ise widget UI'da gizlenir
///   disabledIf : bool sonuclu ifade — true ise widget readonly
///   requiredIf : bool sonuclu ifade — true ise widget zorunlu (statik IsRequired'i override eder)
///   formula    : deger sonuclu ifade — widget readonly, degeri bu ifadeden turer
///
/// Ifadelerdeki tanimlayicilar (w_xxx) ayni form icindeki diger widget'larin
/// WidgetCode'larina referans verir. Frontend expr-eval ile parse edip
/// dependency-driven recompute yapar. Backend sadece string olarak saklar;
/// kural parse / validate isi React tarafinda yapilir, backend minimal
/// whitelist regex'i ile guvenli karakter seti + yasakli kelime kontrolu yapar.
/// </summary>
public sealed record WidgetRulesDto(
    string? VisibleIf,
    string? DisabledIf,
    string? Formula,
    string? RequiredIf = null);

/// <summary>
/// Tek widget icin schema + value birlestirilmis DTO. React'in dogrudan
/// cizebilecegi final form'a denk gelir. Faz C'de hiyerarsi bilgisi
/// (Id, ParentId, SortOrder) eklendi — DynamicWidgetRenderer grup bazli
/// render edebilsin diye.
/// value tipleri:
///   - text / dropdown / link → string
///   - multi-select           → string[]
///   - numeric                → decimal
///   - date                   → "yyyy-MM-dd" string
///   - boolean                → bool
///   - group                  → null (sadece baslik)
///   - lookup                 → string (kullanicinin sectigi ValueColumn degeri)
///   - grid                   → null (Value bos; child satirlar GridRows'da)
///
/// Metadata alani: lookup icin {"guideCode":"CUSTOMERS"}, grid icin
/// {"childFormCode":"SALES_QUOTE_LINES"} gibi anahtar-deger ciftleri tasir.
/// Dropdown/multi-select/link eski `Options` dizisini kullanmaya devam eder.
///
/// GridRows: grid tipi widget'lar icin onceden cozulmus child satirlar.
/// Her satirda recordId + values dictionary bulunur. Grid disi widget'larda null.
/// </summary>
public sealed record WidgetRenderDto(
    int Id,
    int? ParentId,
    int SortOrder,
    string WidgetId,           // WidgetCode
    string Label,
    string DataType,
    IReadOnlyCollection<string>? Options,
    object? Value,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IReadOnlyCollection<GridRowDto>? GridRows = null,
    WidgetRulesDto? Rules = null,
    // DEPRECATED: yerine LabelStyle = "inline" kullanin (DB kolonu korunuyor).
    bool IsPlainField = false,
    bool IsRequired = false,
    int? MaxLength = null,
    int? MinLength = null,
    int? ExpectedLength = null,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    int ColorType = 0,
    string? ColorValue = null,
    int ColSpan = 12,
    string LabelStyle = "standard");

/// <summary>
/// Grid widget'inin tek bir child satirinin serialize edilmis hali.
/// Read path'inde WidgetRenderDto.GridRows, write path'inde SaveGridRow icindeki
/// Values ayni sekli paylasir.
/// </summary>
public sealed record GridRowDto(
    string RecordId,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// Bir formun belirli bir kaydi icin schema + value birlesimi.
/// DynamicWidgetRenderer tek GET cagrisi ile hem form metadatasini hem
/// widget'larini alir.
/// </summary>
public sealed record WidgetRecordDto(
    int FormId,
    string FormCode,
    string FormLabel,
    string RecordId,
    IReadOnlyCollection<WidgetRenderDto> Widgets);

public sealed record SaveWidgetValuesRequest(
    int FormId,
    string RecordId,
    IReadOnlyDictionary<string, object?> Values,
    string? ParentRecordId = null);

/// <summary>
/// Faz E — master-detail save payload. Ana form save endpoint'i bu shape'i
/// kabul eder. `values` ana form widget'larinin flat dict'i (grid/grup haric
/// dogrudan alanlar). `grids` master form'daki her grid widget'i icin child
/// form kodu + satirlari tasir.
///
/// Save orkestrasyonu (WidgetService.SaveRecordAsync):
///   1) Parent kaydet (values → ParentRecordId=NULL ile WidgetTra'ya yazilir)
///   2) Her grid widget icin payload dogrulama (childFormCode eslestir)
///   3) Mevcut child RecordId setini oku (orphan temizligi icin)
///   4) Her child satir icin UpsertValuesAsync (ParentRecordId=parent)
///   5) Eski set - yeni set = orphan → DeleteChildRecordsAsync
///   Hepsi tek transaction icinde.
/// </summary>
public sealed record SaveRecordRequest(
    IReadOnlyDictionary<string, object?>? Values,
    IReadOnlyDictionary<string, SaveGridPayload>? Grids);

public sealed record SaveGridPayload(
    string ChildFormCode,
    IReadOnlyCollection<SaveGridRow> Rows);

public sealed record SaveGridRow(
    string? RecordId,                                  // null → server yeni uretir
    IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// Save orkestrasyonu sonucu. React grid state'i normalize edilmis recordId'leri
/// bu cevaptan alir — ozellikle yeni uretilen child RecordId'leri (null gonderilen
/// satirlarin gercek kimligi) doner.
///
/// gridsNormalized: parent form grid widgetCode → [{tempRecordId?, recordId, ...}]
/// Her child satir icin hem istegin icindeki orijinal kimlik (yoksa null) hem de
/// DB'ye yazilan gercek recordId bulunur, boylece React listeyi 1:1 esleyebilir.
/// </summary>
public sealed record SaveRecordResponseDto(
    bool Success,
    int FormId,
    string RecordId,
    IReadOnlyDictionary<string, IReadOnlyCollection<SaveGridRowNormalized>> Grids);

public sealed record SaveGridRowNormalized(
    string? OriginalRecordId,
    string RecordId);

/// <summary>
/// Admin UI'dan widget tanimi olusturma/guncelleme request'i.
/// Id = null → yeni widget (WidgetMas.Id IDENTITY ile uretilir)
/// Id > 0 → mevcut widget guncellenir
///
/// Options: dropdown / multi-select icin string[] (sadece label'lar,
/// Faz A spec'inde {key,label} cifti yok). Diger tiplerde null gonderilir.
///
/// DataType = 'group' ozel durum: ParentId daima null, Options yok, MaxLength
/// yok. Grup bir widget satiri olarak tutulur ve ParentId self-FK ile field'lar
/// ona bagli olur.
/// </summary>
public sealed record UpsertWidgetRequest(
    int? Id,
    int FormId,
    int? ParentId,
    string WidgetCode,
    string Label,
    string DataType,
    int? MaxLength,
    int SortOrder,
    IReadOnlyCollection<string>? Options,
    bool IsActive = true,
    WidgetRulesDto? Rules = null,
    // DEPRECATED: yerine LabelStyle = "inline" kullanin (DB kolonu korunuyor).
    bool IsPlainField = false,
    bool IsRequired = false,
    int? MinLength = null,
    int? ExpectedLength = null,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    int ColorType = 0,
    string? ColorValue = null,
    int? ColSpan = null,
    string? LabelStyle = null);

public sealed record UpsertWidgetResponse(int Id);

public sealed record FormCatalogItemDto(
    int Id,
    string FormCode,
    string FormName,
    string Module,
    string? SubModule,
    int SortOrder);
