using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Cari grup repository — per-tenant (CompanyId) izole edilmis kayit ve sorgular.
/// Code alani backend'de Name'den turetilir; kullanici Kod girmez (CLAUDE.md kurali).
/// GroupCategory (1-5) MaterialGroups deseninin ayni.
/// ContactGroupMapping 5 slotlu m2m eslestirme.
/// </summary>
public sealed class SqlCariGroupRepository : ICariGroupRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _table;
    private readonly string _mappingTable;

    public SqlCariGroupRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[CariGroup]";
        _mappingTable = $"[{schema}].[ContactGroupMapping]";
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

    public Task<IReadOnlyCollection<CariGroup>> GetAllAsync(CancellationToken cancellationToken)
        => GetAllAsync(null, cancellationToken);

    public async Task<IReadOnlyCollection<CariGroup>> GetAllAsync(int? category, CancellationToken cancellationToken)
    {
        var list = new List<CariGroup>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        var filter = category.HasValue ? " AND [GroupCategory] = @Category" : string.Empty;
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[GroupCategory],[Code],[Name],[SortOrder],[IsActive],[Created]
            FROM {_table}
            WHERE [CompanyId] = @CompanyId AND [IsActive] = 1{filter}
            ORDER BY [GroupCategory], [SortOrder], [Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        if (category.HasValue) cmd.Parameters.Add(new SqlParameter("@Category", category.Value));
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken)) list.Add(Map(rd));
        return list;
    }

    public async Task<CariGroup?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[GroupCategory],[Code],[Name],[SortOrder],[IsActive],[Created]
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
            INSERT INTO {_table} ([CompanyId],[GroupCategory],[Code],[Name],[SortOrder],[IsActive],[Created])
            VALUES (@CompanyId,@GroupCategory,@Code,@Name,@SortOrder,@IsActive,SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", entity.CompanyId > 0 ? entity.CompanyId : GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@GroupCategory", entity.GroupCategory <= 0 ? 1 : entity.GroupCategory));
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
            SET [Name]=@Name, [SortOrder]=@SortOrder, [IsActive]=@IsActive, [GroupCategory]=@GroupCategory
            WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Name", entity.Name));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", entity.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        cmd.Parameters.Add(new SqlParameter("@GroupCategory", entity.GroupCategory <= 0 ? 1 : entity.GroupCategory));
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

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<ContactGroupMappingDto>>> GetContactGroupMappingsBatchAsync(
        IReadOnlyCollection<int> contactIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, IReadOnlyList<ContactGroupMappingDto>>();
        if (contactIds == null || contactIds.Count == 0) return result;

        var ids = contactIds.Distinct().ToArray();
        var paramNames = new string[ids.Length];
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        for (int i = 0; i < ids.Length; i++)
        {
            var p = "@ContactId" + i;
            paramNames[i] = p;
            cmd.Parameters.Add(new SqlParameter(p, ids[i]));
        }
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.CommandText = $"""
            SELECT m.[ContactId], m.[SlotOrder], m.[GroupCode], g.[Name]
            FROM {_mappingTable} m
            LEFT JOIN {_table} g ON g.[Code] = m.[GroupCode] AND g.[GroupCategory] = m.[SlotOrder] AND g.[CompanyId] = @CompanyId
            WHERE m.[ContactId] IN ({string.Join(",", paramNames)})
            ORDER BY m.[ContactId], m.[SlotOrder];
            """;

        var bucket = new Dictionary<int, List<ContactGroupMappingDto>>();
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var contactId = rd.GetInt32(0);
            var dto = new ContactGroupMappingDto(
                rd.GetInt32(1),
                rd.GetString(2),
                rd.IsDBNull(3) ? null : rd.GetString(3));
            if (!bucket.TryGetValue(contactId, out var list))
            {
                list = new List<ContactGroupMappingDto>();
                bucket[contactId] = list;
            }
            list.Add(dto);
        }
        foreach (var kvp in bucket) result[kvp.Key] = kvp.Value;
        return result;
    }

    public async Task SaveContactGroupMappingsAsync(int contactId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = conn.BeginTransaction();

        await using (var delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = transaction;
            delCmd.CommandText = $"DELETE FROM {_mappingTable} WHERE [ContactId]=@ContactId;";
            delCmd.Parameters.Add(new SqlParameter("@ContactId", contactId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (slot, code) in mappings)
        {
            if (string.IsNullOrWhiteSpace(code)) continue;
            await using var insCmd = conn.CreateCommand();
            insCmd.Transaction = transaction;
            insCmd.CommandText = $"INSERT INTO {_mappingTable} ([ContactId],[SlotOrder],[GroupCode]) VALUES (@ContactId,@Slot,@Code);";
            insCmd.Parameters.Add(new SqlParameter("@ContactId", contactId));
            insCmd.Parameters.Add(new SqlParameter("@Slot", slot));
            insCmd.Parameters.Add(new SqlParameter("@Code", code));
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static CariGroup Map(SqlDataReader r) => new()
    {
        Id            = r.GetInt32(0),
        CompanyId     = r.GetInt32(1),
        GroupCategory = r.GetInt32(2),
        Code          = r.GetString(3),
        Name          = r.GetString(4),
        SortOrder     = r.GetInt32(5),
        IsActive      = r.GetBoolean(6),
        Created       = r.GetDateTime(7),
    };
}
