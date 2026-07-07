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
    string LabelStyle = "standard",
    // Sprint 1 — Universal Form Engine. true ise widget Domain entity property'sine baglidir.
    bool IsSystemField = false,
    // Hangi entity property'sine (Pascal-case). Sadece IsSystemField=true ise dolu.
    string? EntityColumn = null,
    // 2026-06-08 — Yetkilendirilebilir alan flag'i. Admin UI'da "Yetkilendirilebilir" switch'i
    // bunu set eder; discovery FIELD:<WidgetCode> izni seed eder. Form render zamanında
    // yetkisiz kullanıcı için alan filtrelenir.
    bool IsPermissionControlled = false);

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
    string? RequiredIf = null,
    // Varsayilan deger — yeni kayit olusturulurken widget'a atanir. Static literal
    // veya formula olabilir (DefaultValueKind ile ayrilir). Tarih alani icin TODAY(),
    // YESTERDAY(), TOMORROW() gibi function call'lar runtime'da cozulur.
    string? DefaultValue = null,
    // 'static' (literal) veya 'formula' (hesaplanan ifade). Null = bilinmiyor / yok.
    string? DefaultValueKind = null);

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
    string LabelStyle = "standard",
    // Sprint 1 — Universal Form Engine. true ise widget Domain entity property'sine baglidir.
    bool IsSystemField = false,
    // Hangi entity property'sine (Pascal-case). Sadece IsSystemField=true ise dolu.
    string? EntityColumn = null);

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
    string? ParentRecordId = null,
    // 2026-07-06 — Zorunlu alan kontrolu (IsRequired + requiredIf) server'da da yapilir.
    // Varsayilan true (guvenli). false yalnizca KISMI deger yazan ic akislar icindir
    // (orn. import handler'i sadece esleştirilmis kolonlari gonderir); UI/API record
    // save akislari her zaman true kalir.
    bool EnforceRequired = true);

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
    string? LabelStyle = null,
    // Sprint 1 — Universal Form Engine. Discovery service tarafindan IsSystemField=true ile
    // seed edilen kayitlar admin UI'dan da degistirilebilir (label, gorunurluk, zorunluluk vb.)
    // ama IsSystemField/EntityColumn semasal kalmali — admin UI bunlari read-only gorur.
    bool IsSystemField = false,
    string? EntityColumn = null,
    // 2026-06-08 — Yetkilendirilebilir alan flag'i. true ise discovery FIELD:<WidgetCode>
    // izni seed eder; izin verilmemiş kullanıcılar form render'da alanı görmez.
    bool IsPermissionControlled = false);

public sealed record UpsertWidgetResponse(int Id);

/// <summary>
/// PATCH /api/widgets/widgets/sort-orders body elemani — yalnizca SortOrder
/// gunceller. Reorder icin tam UpsertWidgetRequest gondermek OptionsJSON'un
/// client tarafinda yeniden insasini gerektiriyordu; lookup/grid/text-rehber
/// tiplerinde metadata (guideCode/guideConfig/childFormCode) kayipli
/// donusuyordu. Bu kontrat o hata sinifini tamamen ortadan kaldirir.
/// </summary>
public sealed record WidgetSortOrderItem(int Id, int SortOrder);

/// <summary>
/// Alan bazli degisiklik gecmisi satiri — GET .../records/{recordId}/history.
/// Label: widget hala mevcutsa guncel etiketi, silinmisse WidgetCode snapshot'i.
/// ParentRecordId dolu ise satir bir grid child kaydina aittir (ChildRecordId
/// hangi kalem oldugunu gosterir).
/// </summary>
public sealed record WidgetValueLogDto(
    long Id,
    string WidgetCode,
    string Label,
    string? OldValue,
    string? NewValue,
    string? ChangedBy,
    DateTime ChangedAt,
    string? ChildRecordId);

// ═══════════════════════════════════════════════════
// Widget tanim transport (export/import) — 2026-07-06
// ═══════════════════════════════════════════════════

/// <summary>
/// Bir formun widget tanim paketi — sirketler arasi kopyalama ve test→canli
/// tasima icin JSON dosyasi olarak indirilir/yuklenir.
///
/// Tasarim notlari:
///   - ParentCode: grup iliskisi Id degil WidgetCode ile tasinir (hedef DB'de
///     Id'ler farklidir).
///   - IsSystemField=true widget'lar pakete dahil EDILMEZ — sistem alanlari
///     hedef ortamda discovery tarafindan kendi EntityColumn eslesmesiyle
///     seed edilir; paketten yazmak yanlis baglama riski tasir.
///   - OptionsJson/RulesJson ham tasinir (kayipsiz); import sirasinda JSON
///     gecerliligi + rule sanitizasyonu yeniden uygulanir.
/// </summary>
public sealed record WidgetPackageDto(
    int CalibraWidgetPackage,          // format versiyonu — su an 1
    string FormCode,
    string? FormLabel,
    DateTime ExportedAt,
    IReadOnlyCollection<WidgetPackageItemDto> Widgets);

public sealed record WidgetPackageItemDto(
    string WidgetCode,
    string Label,
    string DataType,
    string? ParentCode,
    int? MaxLength,
    int? MinLength,
    int? ExpectedLength,
    decimal? MinValue,
    decimal? MaxValue,
    int SortOrder,
    string? OptionsJson,
    string? RulesJson,
    bool IsRequired,
    bool IsActive,
    int ColorType,
    string? ColorValue,
    int ColSpan,
    string? LabelStyle,
    bool IsPermissionControlled);

/// <summary>
/// Import sonucu — kac widget olustu/guncellendi, hangileri neden atlandi.
/// </summary>
public sealed record WidgetImportResultDto(
    int Created,
    int Updated,
    IReadOnlyCollection<string> Skipped);

public sealed record FormCatalogItemDto(
    int Id,
    string FormCode,
    string FormName,
    string Module,
    string? SubModule,
    int SortOrder,
    // 2026-06-09 — ModuleSelector DB-driven entity türetme için
    string? Icon = null,
    string? IconColor = null,
    // 2026-06-09 — Alan Rehberi dropdown filtresi
    // false = container/liste formu, _NEW navigasyon formu veya ayarlar sayfası → gizlenir
    bool IsWidgetForm = true);
