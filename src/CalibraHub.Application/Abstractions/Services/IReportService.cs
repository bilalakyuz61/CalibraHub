using System.Data;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// FastReport Open Source ile .frx sablonlarini PDF veya HTML olarak uretir.
/// </summary>
public interface IReportService
{
    Task<byte[]> ExportPdfAsync(string frxFilePath, DataTable data, CancellationToken ct = default);
    Task<string> ExportHtmlAsync(string frxFilePath, DataTable data, CancellationToken ct = default);

    /// <summary>DB'de saklanan .frx binary icerigi ile PDF uretir.</summary>
    Task<byte[]> ExportPdfFromBytesAsync(byte[] frxContent, DataTable data, CancellationToken ct = default);
    /// <summary>DB'de saklanan .frx binary icerigi ile HTML uretir.</summary>
    Task<string> ExportHtmlFromBytesAsync(byte[] frxContent, DataTable data, CancellationToken ct = default);

    /// <summary>
    /// Coklu data source ile PDF uretir. Her key = frx'te beklenen source adi
    /// ("Belge", "Kombinasyon", "Cari" vb.), her value = runtime DataTable.
    /// </summary>
    Task<byte[]> ExportPdfFromBytesAsync(byte[] frxContent, IReadOnlyDictionary<string, DataTable> sources, CancellationToken ct = default);

    /// <summary>Coklu source HTML uretimi.</summary>
    Task<string> ExportHtmlFromBytesAsync(byte[] frxContent, IReadOnlyDictionary<string, DataTable> sources, CancellationToken ct = default);
}
