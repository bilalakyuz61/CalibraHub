using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Veri kaynağı metadata — alias bazında master-detail nesting için kullanılır.
/// ParentAlias: bu kaynağın altına nest edileceği üst kaynağın alias'ı.
/// JoinOn: parent ile child arasında eşleşecek kolon adı (her iki view'da da var olmalı).
/// </summary>
public sealed record DataSourceMeta(string Alias, string? ParentAlias, string? JoinOn);

public interface IDocLayoutRenderer
{
    byte[] RenderPdf(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data,
                     IReadOnlyList<DataSourceMeta>? sources = null);
    string RenderHtml(string layoutJson, IReadOnlyDictionary<string, ReportRawResult> data,
                      IReadOnlyList<DataSourceMeta>? sources = null);
}
