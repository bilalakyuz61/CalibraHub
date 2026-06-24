using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDbSchemaService
{
    Task<IReadOnlyList<DbTableSummaryDto>> GetTablesAsync(CancellationToken cancellationToken);

    Task<DbTableDetailDto?> GetTableDetailAsync(string schema, string name, CancellationToken cancellationToken);

    /// <summary>Tum semayi Mermaid ER diagram formatinda donusturur.</summary>
    Task<string> BuildMermaidErAsync(CancellationToken cancellationToken);

    /// <summary>Tum semayi CSV formatinda donusturur (tablo x kolon satirlari).</summary>
    Task<string> BuildCsvAsync(CancellationToken cancellationToken);

    /// <summary>Tum semayi Markdown dokumanina donusturur (tablolar + FK'ler).</summary>
    Task<string> BuildMarkdownAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Tüm view'ları döner: CalibraViewCatalog view'ları + DB'de bulunan kullanıcı view'ları.
    /// Açıklamalar: katalog view'ları için statik, kullanıcı view'ları için ViewMeta tablosundan.
    /// </summary>
    Task<IReadOnlyList<DbViewInfoDto>> GetViewsAsync(CancellationToken cancellationToken);

    /// <summary>Bir view için kullanıcı açıklamasını ViewMeta tablosuna kaydeder.</summary>
    Task SaveViewDescriptionAsync(string viewName, string? description, string updatedBy, CancellationToken cancellationToken);
}
