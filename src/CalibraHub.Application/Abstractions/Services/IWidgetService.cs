using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// EAV widget sisteminin tercuman katmani.
///
/// "Zeki Veri, Aptal Bilesen" mimarisinde bu servis:
///   - DataType'a gore validation yapar (numeric/date/boolean parse, dropdown
///     kontrolu, multi-select array serialization, MaxLength kontrolu)
///   - Schema + value birlesik DTO'lar hazirlar (React dogrudan cizer)
///   - Paralel calisir: mevcut IDynamicFieldService'i bozmaz, yeni tablolarda
///     (WidgetMas, WidgetTra) yasar
/// </summary>
public interface IWidgetService
{
    Task<WidgetFormSchemaDto?> GetFormSchemaAsync(int formId, CancellationToken ct);
    Task<WidgetFormSchemaDto?> GetFormSchemaByCodeAsync(string formCode, CancellationToken ct);

    /// <summary>
    /// Belirli bir kayit icin schema + value'yu render edilmeye hazir DTO
    /// listesi olarak doner. React bu listeyi dogrudan cizer.
    /// Grup (DataType='group') satirlari filtrelenmez — UI kararina birakilir.
    /// </summary>
    Task<IReadOnlyCollection<WidgetRenderDto>> GetRenderModelAsync(
        int formId,
        string recordId,
        CancellationToken ct);

    /// <summary>
    /// Faz C — DynamicWidgetRenderer icin: formCode + recordId alir,
    /// schema + value birlesik wrapper DTO doner. Tek round trip.
    /// Form bulunamazsa null.
    /// </summary>
    Task<WidgetRecordDto?> GetRecordByCodeAsync(
        string formCode,
        string recordId,
        CancellationToken ct);

    /// <summary>
    /// Kullanici input'unu valide eder, DataType'a gore string'e serialize eder,
    /// repository'ye upsert eder.
    /// Hata durumunda ArgumentException firlatir (controller 400 doner).
    /// parentRecordId: master-detail icin — child satir kaydinda parent kaydini
    /// belirtir. Master kayitta null.
    /// </summary>
    Task SaveValuesAsync(SaveWidgetValuesRequest request, CancellationToken ct);

    /// <summary>
    /// Faz E — master-detail save orkestrasyonu.
    ///
    /// Akis:
    ///   1) Parent kaydet (request.Values → WidgetTra, ParentRecordId=NULL)
    ///   2) Her grid widget icin payload dogrulama + child save + orphan temizligi
    ///   3) Tek transaction icinde — hata olursa hicbir sey yazilmaz (nested
    ///      SaveValuesAsync cagrilariyla ayni connection/tx paylasimi saglanir)
    ///
    /// Donus: save'den sonra normalize edilmis child RecordId'leri (yeni uretilenler
    /// dahil) — React tarafi grid state'i bu bilgiyle gunceller.
    /// </summary>
    Task<SaveRecordResponseDto> SaveRecordAsync(
        int formId,
        string recordId,
        SaveRecordRequest request,
        CancellationToken ct);

    // ═══════════════════════════════════════════════════
    // Admin UI icin tanim CRUD metodlari (Faz B)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Tum aktif form kataloglarini listeler. Admin UI module selector'unu
    /// besler. Whitelist kontrolu controller tarafinda yapilir (Faz B: sadece
    /// yeni API'ye gecmis formlar).
    /// </summary>
    Task<IReadOnlyCollection<FormCatalogItemDto>> GetFormsAsync(CancellationToken ct);

    /// <summary>
    /// Widget tanimini olusturur veya gunceller. Options string[] ise JSON
    /// serialize edilip OptionsJson kolonuna yazilir. WidgetCode normalize
    /// edilir (lowercase, a-z0-9_, harfle baslamali).
    /// </summary>
    Task<int> UpsertWidgetAsync(UpsertWidgetRequest request, CancellationToken ct);

    /// <summary>
    /// Widget tanimini ve bagli WidgetTra degerlerini siler (transaction).
    /// </summary>
    Task DeleteWidgetAsync(int widgetId, CancellationToken ct);

    /// <summary>
    /// SmartBoard liste ekranlari icin: birden fazla kaydin widget degerlerini
    /// tek round-trip ile toplu ceker. Donus: recordId → render DTO listesi.
    /// Grup ve grid widget'lar filtrelenir — sadece duz alanlar doner.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<WidgetRenderDto>>> GetBatchRenderModelsAsync(
        string formCode,
        IReadOnlyCollection<string> recordIds,
        CancellationToken ct);

    /// <summary>
    /// Bir widget'in IsPlainField bayragi guncellenir (tam upsert yerine tekil guncelleme).
    /// Widget bulunamazsa KeyNotFoundException firlatir.
    /// </summary>
    Task ToggleIsPlainFieldAsync(int widgetId, bool isPlainField, CancellationToken ct);
}
