using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Harici SQL Server kaynağından SALT-OKUNUR erişim: bağlantı testi, şema keşfi ve
/// satır okuma. Bu arayüzün hiçbir implementasyonu kaynak veritabanına yazmaz —
/// üretilen tek ifade türü SELECT'tir ve şema/tablo/kolon adları kullanıcı metninden
/// değil, kaynağın kendi <c>sys</c> kataloğundan doğrulanarak köşeli ayraçla kaçırılır
/// (SQL injection savunması).
/// </summary>
public interface IExternalDbReader
{
    /// <summary>Bağlantıyı dener; sunucu sürümünü döner. Exception fırlatmaz.</summary>
    Task<ExternalDbTestResultDto> TestAsync(ExternalDbConnection connection, CancellationToken ct);

    /// <summary>Okunabilir tablo + view listesi (sistem nesneleri hariç).</summary>
    Task<IReadOnlyList<ExternalDbObjectDto>> ListObjectsAsync(
        ExternalDbConnection connection, CancellationToken ct);

    /// <summary>Verilen tablo/view'ın kolonları. Nesne yoksa boş liste döner.</summary>
    Task<IReadOnlyList<ExternalDbColumnDto>> ListColumnsAsync(
        ExternalDbConnection connection, string schemaName, string objectName, CancellationToken ct);

    /// <summary>
    /// Satırları okur. <paramref name="columns"/> boşsa nesnenin tüm kolonları alınır.
    /// <paramref name="maxRows"/> üst sınırdır (önizlemede küçük, aktarımda güvenlik tavanı).
    /// Değerler <c>ImportParse</c> ile uyumlu biçimde metne çevrilir:
    /// ondalık invariant ("1234.56"), tarih "yyyy-MM-dd[ HH:mm:ss]", bit "1"/"0".
    /// </summary>
    Task<ExternalDbSampleDto> ReadRowsAsync(
        ExternalDbConnection connection,
        string schemaName,
        string objectName,
        IReadOnlyCollection<string>? columns,
        int maxRows,
        CancellationToken ct);
}
