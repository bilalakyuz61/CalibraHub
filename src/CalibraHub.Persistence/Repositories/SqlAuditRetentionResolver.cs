using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Şirket bazlı audit saklama süresini okur. Background temizlik döngüsünden
/// çağrıldığı için HttpContext'e dayanmaz — bağlantı doğrudan
/// CompanyConnectionRegistry'deki şirket connection string'i ile açılır.
/// Parametre: CompanyParameter (FormCode=SECURITY, ParamKey=AUDIT_RETENTION_DAYS).
/// </summary>
public sealed class SqlAuditRetentionResolver : IAuditRetentionResolver
{
    private readonly CompanyConnectionRegistry _registry;
    private readonly string _schema;

    public SqlAuditRetentionResolver(CompanyConnectionRegistry registry, CalibraDatabaseOptions options)
    {
        _registry = registry;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<int> GetRetentionDaysAsync(int companyId, CancellationToken ct)
    {
        if (!_registry.TryGet(companyId, out var connectionString))
            return AuditParameters.DefaultRetentionDays;

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT TOP 1 [ParamValue]
                FROM [{_schema.Replace("]", "]]")}].[CompanyParameter]
                WHERE [FormCode] = @FormCode AND [ParamKey] = @ParamKey
                """;
            cmd.Parameters.AddWithValue("@FormCode", AuditParameters.FormCode);
            cmd.Parameters.AddWithValue("@ParamKey", AuditParameters.RetentionDaysKey);

            var raw = await cmd.ExecuteScalarAsync(ct) as string;
            return int.TryParse(raw, out var days) && days >= 0
                ? days
                : AuditParameters.DefaultRetentionDays;
        }
        catch
        {
            // DB erişilemedi (bakım, bağlantı sorunu) → varsayılanla devam; silme agresifleşmesin
            return AuditParameters.DefaultRetentionDays;
        }
    }
}
