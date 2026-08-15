using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Security;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Services;

/// <summary>
/// <see cref="ExternalDbConnection"/> tanımından ADO.NET bağlantı dizesi üretir.
/// Tek kaynak: hem salt-okunur okuyucu (<see cref="SqlServerExternalDbReader"/>) hem
/// kaynak-DB prosedür çalıştırıcısı bunu kullanır — parola çözme ve TLS ayarları
/// iki yerde kopyalanmaz.
/// </summary>
internal static class ExternalDbConnectionStringBuilder
{
    public static string Build(ExternalDbConnection s, string applicationName)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = s.ServerName?.Trim() ?? string.Empty,
            InitialCatalog = s.DatabaseName?.Trim() ?? string.Empty,
            Encrypt = s.Encrypt,
            TrustServerCertificate = s.TrustServerCertificate,
            ConnectTimeout = s.ConnectTimeoutSeconds <= 0 ? 15 : s.ConnectTimeoutSeconds,
            ApplicationName = applicationName,
            Pooling = true,
        };

        if (s.IsIntegratedSecurity)
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.IntegratedSecurity = false;
            b.UserID = s.Username?.Trim() ?? string.Empty;
            b.Password = ExternalDbSecretProtector.Unprotect(s.PasswordEncrypted ?? string.Empty);
        }

        return b.ConnectionString;
    }

    public static async Task<SqlConnection> OpenAsync(
        ExternalDbConnection settings, string applicationName, CancellationToken ct)
    {
        var conn = new SqlConnection(Build(settings, applicationName));
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }
}
