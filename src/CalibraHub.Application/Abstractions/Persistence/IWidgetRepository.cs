using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// EAV widget sisteminin persistence arayuzu.
/// Tablolar: dbo.Forms (mevcut), dbo.WidgetMas (yeni), dbo.WidgetTra (yeni).
///
/// Eski IDynamicFieldValueRepository / ILogisticsConfigurationRepository ile
/// paralel calisir; hicbirini cagirmaz veya bozmaz.
/// </summary>
public interface IWidgetRepository
{
    // ── Forms (mevcut dbo.Forms) ─────────────────
    Task<IReadOnlyCollection<FormDefinition>> GetFormsAsync(CancellationToken ct);
    Task<FormDefinition?> GetFormByIdAsync(int formId, CancellationToken ct);
    Task<FormDefinition?> GetFormByCodeAsync(string formCode, CancellationToken ct);

    // ── WidgetMas (widget tanimlari) ────────────
    /// <param name="includeInactive">true → pasif widget'lar da dahil edilir (admin panel icin)</param>
    Task<IReadOnlyCollection<WidgetDefinition>> GetWidgetsByFormAsync(int formId, CancellationToken ct, bool includeInactive = false);
    Task<WidgetDefinition?> GetWidgetByIdAsync(int widgetId, CancellationToken ct);

    /// <summary>
    /// Yeni widget olusturur (Id=0) veya mevcut widget'i gunceller (Id>0).
    /// Returns: persist edilmis widget'in Id'si.
    /// </summary>
    Task<int> UpsertWidgetAsync(WidgetDefinition widget, CancellationToken ct);

    Task DeleteWidgetAsync(int widgetId, CancellationToken ct);

    /// <summary>
    /// Bir grup (veya herhangi bir widget) altindaki dogrudan cocuk widget sayisini
    /// doner. Grup silme guard'i icin kullanilir — cocuklu grup silinemez.
    /// IsActive filtresi uygulamaz; herhangi bir ParentId referansi sayilir.
    /// </summary>
    Task<int> CountChildrenByParentIdAsync(int parentId, CancellationToken ct);

    // ── WidgetTra (widget degerleri) ────────────
    /// <summary>
    /// Bir formun belirli bir kaydi icin tum widget degerlerini doner.
    /// (WidgetTra INNER JOIN WidgetMas ON FormId filter ile).
    /// </summary>
    Task<IReadOnlyCollection<WidgetValue>> GetValuesAsync(int formId, string recordId, CancellationToken ct);

    /// <summary>
    /// Birden fazla kayit icin widget degerlerini tek sorguda toplu ceker.
    /// Donus: recordId → WidgetValue koleksiyonu lookup (bos kayit = bos koleksiyon).
    /// SmartBoard liste ekranlari icin N+1 problemini ortadan kaldirir.
    /// </summary>
    Task<ILookup<string, WidgetValue>> GetValuesBatchAsync(int formId, IReadOnlyCollection<string> recordIds, CancellationToken ct);

    /// <summary>
    /// Bir formun kaydi icin widget degerlerini toplu upsert eder.
    /// valuesByWidgetId: key = WidgetMas.Id, value = serialize edilmis string (null = sil).
    /// Transaction icinde DELETE + INSERT stratejisi (scope: sadece verilen widget id'lerine sahip satirlar).
    /// parentRecordId: master-detail icin — child satirlarin parent kaydina referansi.
    /// NULL ise kayit kendisi master'dir.
    /// </summary>
    Task UpsertValuesAsync(
        int formId,
        string recordId,
        IReadOnlyDictionary<int, string?> valuesByWidgetId,
        CancellationToken ct,
        string? parentRecordId = null);

    /// <summary>
    /// Widget degerlerini bir RecordId'den digerine kopyalar (INSERT ... SELECT).
    /// Kalem revizyonu gibi "birebir kopya" senaryolarinda kullanilir. Sadece
    /// top-level (ParentRecordId IS NULL) degerleri kopyalanir — grid child
    /// satirlari atlanir (yeni revize icin grid'i bastan olusturmak normal UX).
    /// </summary>
    Task CopyValuesAsync(
        int formId,
        string sourceRecordId,
        string targetRecordId,
        CancellationToken ct);

    // ── Master-Detail (Faz E — grid widget) ────────────

    /// <summary>
    /// Bir parent kaydi icin verilen child form'a bagli tum child RecordId'leri doner.
    /// Orn: parent=TK-2025-0001 → SALES_QUOTE_LINES form'unun altinda 5 child satir varsa
    /// 5 RecordId donulur. Orphan temizligi icin save orkestrasyonu tarafindan kullanilir.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetChildRecordIdsAsync(
        int childFormId,
        string parentRecordId,
        CancellationToken ct);

    /// <summary>
    /// Verilen child RecordId listesine ait tum WidgetTra satirlarini siler.
    /// Save akisinda orphan temizligi icin: parent save sonrasi istek disinda
    /// kalan eski child satirlari bu metodla kaldirilir.
    /// </summary>
    Task DeleteChildRecordsAsync(
        int childFormId,
        IReadOnlyCollection<string> childRecordIds,
        CancellationToken ct);

    // ── Flattened View (Faz H) ────────────────────────────────

    /// <summary>
    /// Verilen form icin v_Flat_{FormCode} dinamik view'ini olusturur veya
    /// gunceller (CREATE OR ALTER VIEW). Form.BaseTable + BaseRecordKey dolu
    /// olmali; yoksa metod no-op olarak doner.
    ///
    /// View SQL sematik:
    ///   SELECT base.*,
    ///          TRY_CAST(MAX(CASE WHEN m.WidgetCode='w_a' THEN t.Value END) AS ...) AS [w_a],
    ///          ...
    ///     FROM {BaseTable} base
    ///     LEFT JOIN dbo.WidgetTra t ON t.RecordId = base.{BaseRecordKey}
    ///     LEFT JOIN dbo.WidgetMas m ON m.Id = t.WidgetId AND m.FormId = {form.Id}
    ///    GROUP BY base.*
    ///
    /// Tum identifier'lar (BaseTable, BaseRecordKey, WidgetCode) regex allowlist'ten
    /// gecer, [bracket] ile escape edilir. Widget'lar grup/grid disindaki aktif
    /// alanlardan secilir.
    ///
    /// Hata durumunda ArgumentException firlatir (invalid identifier, base tablo
    /// bulunamadi vs.). Service layer bu hatayi loglayip widget save'i basarili
    /// sayar — view sadece rapor kolayligidir, kritik path degil.
    /// </summary>
    Task RegenerateFlattenedViewAsync(
        FormDefinition form,
        IReadOnlyCollection<WidgetDefinition> widgets,
        CancellationToken ct);

    /// <summary>
    /// Tum BaseTable'li formlar icin view'lari tek seferde yeniden olusturur.
    /// Startup hook'u — DB restore, upgrade veya initial deploy sonrasi view'lar
    /// mevcut widget state'ine hizalanir. Her bir form icin sessiz try/catch,
    /// tek bir form'un hatasi digerlerini engellemez.
    /// </summary>
    Task RegenerateAllFlattenedViewsAsync(CancellationToken ct);
}
