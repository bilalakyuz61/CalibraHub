using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Aktif sirket DB'sinden fiziksel sema metadata'si okur (sys.* views).
/// Sadece metadata; ornek veri / PII okunmaz.
/// </summary>
public interface IDbSchemaRepository
{
    Task<IReadOnlyList<DbTableSummaryDto>> GetTablesAsync(CancellationToken cancellationToken);

    Task<DbTableDetailDto?> GetTableDetailAsync(string schema, string name, CancellationToken cancellationToken);

    /// <summary>Tum FK'leri tek sorguda doner (Mermaid ER export icin).</summary>
    Task<IReadOnlyList<DbForeignKeyDto>> GetAllForeignKeysAsync(CancellationToken cancellationToken);

    /// <summary>Sistem DB'sindeki user-defined view adlarini doner (ViewReport task icin).</summary>
    Task<IReadOnlyList<string>> GetViewNamesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Report Designer kaynak seçicisi için tüm dbo view'larını kolon
    /// metadata'sıyla döner. Sayısal kolonlar metrik, tarih/metin kolonlar grup
    /// olarak sınıflandırılır.
    /// </summary>
    Task<IReadOnlyList<RdViewInfo>> GetDesignerViewsAsync(CancellationToken cancellationToken);

    /// <summary>ViewMeta tablosundan kullanıcı tanımlı açıklamaları döner (ViewName → Description).</summary>
    Task<IReadOnlyDictionary<string, string>> GetViewMetaAsync(CancellationToken cancellationToken);

    /// <summary>ViewMeta tablosuna view açıklaması yazar (MERGE — yoksa INSERT, varsa UPDATE).</summary>
    Task SaveViewMetaAsync(string viewName, string? description, string updatedBy, CancellationToken cancellationToken);
}
