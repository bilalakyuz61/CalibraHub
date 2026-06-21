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
    /// CalibraViewCatalog'daki tum view'lari doner; fiziksel varlik ve kolon
    /// bilgisi DB'den (sys.columns), aciklama / kullanim yeri katalogdan gelir.
    /// </summary>
    Task<IReadOnlyList<DbViewInfoDto>> GetViewsAsync(CancellationToken cancellationToken);
}
