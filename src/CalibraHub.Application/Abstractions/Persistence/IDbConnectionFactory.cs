using System.Data.Common;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Per-company veritabanı bağlantısı açan soyut fabrika.
/// Application katmanı SqlClient'i doğrudan referans vermez; implementasyon Persistence'tadır.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken ct);
}
