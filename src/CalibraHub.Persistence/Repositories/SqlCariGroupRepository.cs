using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Cari grup repository — per-tenant (CompanyId) izole edilmis kayit ve sorgular.
/// Code alani backend'de Name'den turetilir; kullanici Kod girmez (CLAUDE.md kurali).
/// </summary>
public sealed class SqlCariGroupRepository : ICariGroupRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _table;

    public SqlCariGroupRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[CariGroup]";
    }

    private int GetCurrentCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var raw = httpContext.User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var id))
                return id;
        }
        return 1; // default tenant fallback
    }

    public async Task<IReadOnlyCollection<CariGroup>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<CariGroup>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[Code],[Name],[SortOrder],[IsActive],[CreatedAt]
            FROM {_table}
            WHERE [CompanyId] = @CompanyId AND [IsActive] = 1
            ORDER BY [SortOrder], [Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken)) list.Add(Map(rd));
        return list;
    }

    public async Task<CariGroup?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[Code],[Name],[SortOrder],[IsActive],[CreatedAt]
            FROM {_table}
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        return await rd.ReadAsync(cancellationToken) ? Map(rd) : null;
    }

    public async Task<int> AddAsync(CariGroup entity, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table} ([CompanyId],[Code],[Name],[SortOrder],[IsActive],[CreatedAt])
            VALUES (@CompanyId,@Code,@Name,@SortOrder,@IsActive,SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", entity.CompanyId > 0 ? entity.CompanyId : GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Code", entity.Code));
        cmd.Parameters.Add(new SqlParameter("@Name", entity.Name));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(CariGroup entity, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET [Name]=@Name, [SortOrder]=@SortOrder, [IsActive]=@IsActive
            WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Name", entity.Name));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        // Soft delete — kayitlari (Contact.ContactGroupId, DocLayoutRule.ContactGroupId)
        // bozmamak icin IsActive=0 yap; gercek silme buyuk veri yikimina yol acabilir.
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table} SET [IsActive]=0
            WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CariGroup Map(SqlDataReader r) => new()
    {
        Id        = r.GetInt32(0),
        CompanyId = r.GetInt32(1),
        Code      = r.GetString(2),
        Name      = r.GetString(3),
        SortOrder = r.GetInt32(4),
        IsActive  = r.GetBoolean(5),
        CreatedAt = r.GetDateTime(6),
    };
}
