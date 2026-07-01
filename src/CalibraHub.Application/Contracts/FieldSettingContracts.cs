namespace CalibraHub.Application.Contracts;

/// <summary>
/// FldSet tablosundan okunan alan ayari — admin UI liste/duzenleme icin.
/// PR 1+: ViewName primary; GuideCode geriye uyumluluk (PR 3'te dusurulecek).
/// </summary>
public sealed record FieldSettingDto(
    int Id,
    int FormId,
    string FieldKey,
    string FieldLabel,
    string? GuideCode,
    string? ViewName,
    string? FilterJson,
    bool IsRequired,
    string? FormatJson,
    bool IsActive,
    int SortOrder);

/// <summary>
/// Tekil alan ayari ekleme/guncelleme istegi.
/// Id=0 yeni, Id>0 guncelleme.
/// </summary>
public sealed record UpsertFieldSettingRequest(
    int Id,
    int FormId,
    string FieldKey,
    string FieldLabel,
    string? GuideCode,
    string? ViewName,
    string? FilterJson,
    bool IsRequired,
    string? FormatJson,
    bool IsActive,
    int SortOrder);

/// <summary>
/// Toplu rehber eslestirme istegi — eslestirme modali "Kaydet" butonu.
/// Bir rehber icin belirli bir formun alanlarina toplu eslestirme/kaldir.
/// PR 1: GuideCode'tan ViewName cikarsama otomatik (GuideMas'a bakilir);
/// PR 3'te imza ViewName'e gecirilecek.
/// </summary>
public sealed record BulkMapGuideRequest(
    string GuideCode,
    int FormId,
    IReadOnlyCollection<FieldMappingItem> Fields);

/// <summary>
/// Toplu eslestirme icin tekil alan bilgisi.
/// Mapped=true ise GuideCode baglanir, false ise NULL yapilir.
/// IsRequired=true ise alan kayit sirasinda zorunlu tutulur.
/// </summary>
public sealed record FieldMappingItem(
    string FieldKey,
    string FieldLabel,
    bool Mapped,
    string? FilterJson,
    bool IsRequired = false);

/// <summary>
/// Runtime: form sayfasinin yüklendiginde ihtiyac duydugu alan-rehber baglantisi.
/// FormatJson schema: { visibleColumns: [], columnLabels: {}, valueColumn, displayColumn, sortColumn?, distinctTextual? }
/// PR 1+: GuideCode geriye uyumluluk icin doldurulur; runtime ViewName'i tercih edebilir.
/// </summary>
public sealed record FieldGuideBindingDto(
    string FieldKey,
    string FieldLabel,
    string GuideCode,
    string? ViewName,
    string? FilterJson,
    bool IsRequired,
    string? FormatJson,
    string? RequiredTags = null);

/// <summary>
/// GuideLookupCell Ayarlar panelinden gelen upsert istegi.
/// FormCode kullanir (formId yerine) — grid hucresi formId bilmez.
/// </summary>
public sealed record UpsertFieldSettingByFormCodeRequest(
    string FormCode,
    string FieldKey,
    string FieldLabel,
    string? GuideCode,
    string? ViewName,
    string? FilterJson,
    bool IsRequired,
    string? FormatJson);
