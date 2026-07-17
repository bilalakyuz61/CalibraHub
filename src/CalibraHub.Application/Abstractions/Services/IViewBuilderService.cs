using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// SQL View Yönetimi — CLAUDE.md "SQL View Yönetimi — Kontrollü İstisna" (2026-07-17).
/// Mevcut view'ları (join/hesaplanan alan ile) genişletme + sıfırdan özel view kurma.
/// SystemAdmin-only araç — bkz. ViewBuilderController.
/// </summary>
public interface IViewBuilderService
{
    // ── Şema yardımcıları (görsel kurucu dropdown'ları) ────────────────
    Task<IReadOnlyList<ViewSchemaTableDto>> GetSchemaObjectsAsync(CancellationToken ct);
    Task<IReadOnlyList<ViewSchemaColumnDto>> GetSchemaColumnsAsync(string schema, string name, CancellationToken ct);

    // ── View listeleme / tanım okuma ───────────────────────────────────
    Task<IReadOnlyList<ViewListItemDto>> ListViewsAsync(CancellationToken ct);
    Task<ViewDetailDto?> GetViewDetailAsync(string viewName, CancellationToken ct);

    // ── SQL üretimi + doğrulama ─────────────────────────────────────────
    /// <summary>Definition JSON'dan SELECT gövdesi üretir. Tüm identifier'lar sys.tables/
    /// sys.columns'a karşı doğrulanır; sonuçtaki tam gövde ayrıca ValidateRawSql guard'larından
    /// geçer (calc kolon expression'ları serbest metin olduğundan defense-in-depth).</summary>
    Task<ViewGenerateResult> GenerateSqlAsync(ViewDefinitionJson definition, CancellationToken ct);

    /// <summary>Yalnız SELECT + tek ifade + bilinen tablo/view referansı doğrulaması (ScriptDom) +
    /// transaction içinde CREATE OR ALTER VIEW deneyip ROLLBACK (gerçek derlenebilirlik testi).
    /// viewName boş verilirse geçici/rastgele bir prob adıyla test edilir. Temiz ise null döner.</summary>
    Task<string?> ValidateRawSqlAsync(string? viewName, string sql, CancellationToken ct);

    /// <summary>Örnek satır + kolon listesi + kartezyen sinyali. Hiçbir kalıcı değişiklik yapmaz.</summary>
    Task<ViewPreviewResult> PreviewAsync(PreviewViewRequest request, CancellationToken ct);

    /// <summary>Doğrula + (SystemExtend ise) non-breaking guard + CREATE OR ALTER VIEW + eski
    /// override varsa ViewDefinitionRevision'a snapshot + ViewDefinition upsert. Tek transaction.</summary>
    Task<ViewApplyResult> ApplyAsync(ApplyViewRequest request, string? username, CancellationToken ct);

    /// <summary>En son revizyonun GeneratedSql'ini yeniden apply eder (aynı guard'lardan geçer;
    /// revert öncesi hal de yeni bir revizyon olarak loglanır — geri alınabilir geri alma).</summary>
    Task<ViewApplyResult> RevertAsync(int viewDefinitionId, string? username, CancellationToken ct);
}
