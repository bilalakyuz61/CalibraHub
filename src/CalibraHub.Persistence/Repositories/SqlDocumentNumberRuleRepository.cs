using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — DocumentNumberRule CRUD + Counter state operations.
/// Per-company DB (CompanyId yok, CalibraHub multi-tenant connection routing kullanır).
/// </summary>
public sealed class SqlDocumentNumberRuleRepository : IDocumentNumberRuleRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _counterTable;

    public SqlDocumentNumberRuleRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table        = $"[{schema}].[DocumentNumberRule]";
        _counterTable = $"[{schema}].[DocumentNumberCounter]";
    }

    public async Task<IReadOnlyCollection<DocumentNumberRule>> ListAsync(CancellationToken ct)
    {
        var list = new List<DocumentNumberRule>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[DocumentTypeId],
                   [ContactId],[ContactGroupId],[UserId],[BranchId],[FromDate],[ToDate],
                   [Prefix],[YearFormat],[MonthFormat],[CounterLength],[CounterStart],
                   [ResetPeriod],[TotalLength],[Weight],[IsActive],
                   [CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM {_table}
            ORDER BY [Weight] DESC, [Name];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list;
    }

    public async Task<DocumentNumberRule?> GetAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[Name],[DocumentTypeId],
                   [ContactId],[ContactGroupId],[UserId],[BranchId],[FromDate],[ToDate],
                   [Prefix],[YearFormat],[MonthFormat],[CounterLength],[CounterStart],
                   [ResetPeriod],[TotalLength],[Weight],[IsActive],
                   [CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM {_table} WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<int> SaveAsync(DocumentNumberRule rule, CancellationToken ct)
    {
        // Ağırlık otomatik hesap (DocLayoutRule pattern: Cari=16+Grup=8+User=4+Şube=2+Tarih=1)
        rule.Weight = ComputeWeight(rule);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (rule.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_table} SET
                  [Name]=@Name,[DocumentTypeId]=@DocumentTypeId,
                  [ContactId]=@ContactId,[ContactGroupId]=@ContactGroupId,[UserId]=@UserId,
                  [BranchId]=@BranchId,[FromDate]=@FromDate,[ToDate]=@ToDate,
                  [Prefix]=@Prefix,[YearFormat]=@YearFormat,[MonthFormat]=@MonthFormat,
                  [CounterLength]=@CounterLength,[CounterStart]=@CounterStart,
                  [ResetPeriod]=@ResetPeriod,[TotalLength]=@TotalLength,
                  [Weight]=@Weight,[IsActive]=@IsActive,
                  [UpdatedBy]=@UpdatedBy,[Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", rule.Id));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_table}
                  ([Name],[DocumentTypeId],
                   [ContactId],[ContactGroupId],[UserId],[BranchId],[FromDate],[ToDate],
                   [Prefix],[YearFormat],[MonthFormat],[CounterLength],[CounterStart],
                   [ResetPeriod],[TotalLength],[Weight],[IsActive],[CreatedBy])
                OUTPUT INSERTED.[Id]
                VALUES
                  (@Name,@DocumentTypeId,
                   @ContactId,@ContactGroupId,@UserId,@BranchId,@FromDate,@ToDate,
                   @Prefix,@YearFormat,@MonthFormat,@CounterLength,@CounterStart,
                   @ResetPeriod,@TotalLength,@Weight,@IsActive,@CreatedBy);
                """;
        }
        AddParams(cmd, rule);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id]=@Id;";  // counters CASCADE düşer
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<DocumentNumberCounter>> GetCountersAsync(int ruleId, CancellationToken ct)
    {
        var list = new List<DocumentNumberCounter>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[RuleId],[ResetKey],[CurrentValue],[LastUpdated]
            FROM {_counterTable} WHERE [RuleId]=@RuleId
            ORDER BY [ResetKey] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@RuleId", ruleId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocumentNumberCounter
            {
                Id           = reader.GetInt32(0),
                RuleId       = reader.GetInt32(1),
                ResetKey     = reader.GetString(2),
                CurrentValue = reader.GetInt64(3),
                LastUpdated  = reader.GetDateTime(4),
            });
        }
        return list;
    }

    public async Task ResetCounterAsync(int ruleId, string resetKey, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_counterTable}
            SET [CurrentValue]=0, [LastUpdated]=SYSUTCDATETIME()
            WHERE [RuleId]=@RuleId AND [ResetKey]=@ResetKey;
            """;
        cmd.Parameters.Add(new SqlParameter("@RuleId", ruleId));
        cmd.Parameters.Add(new SqlParameter("@ResetKey", resetKey ?? string.Empty));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static int ComputeWeight(DocumentNumberRule r)
    {
        int w = 0;
        if (r.ContactId      is > 0) w += 16;
        if (r.ContactGroupId is > 0) w += 8;
        if (r.UserId         is > 0) w += 4;
        if (r.BranchId       is > 0) w += 2;
        if (r.FromDate.HasValue || r.ToDate.HasValue) w += 1;
        return w;
    }

    private static void AddParams(SqlCommand cmd, DocumentNumberRule r)
    {
        cmd.Parameters.Add(new SqlParameter("@Name", r.Name));
        cmd.Parameters.Add(new SqlParameter("@DocumentTypeId", r.DocumentTypeId));
        cmd.Parameters.Add(new SqlParameter("@ContactId",      (object?)r.ContactId      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactGroupId", (object?)r.ContactGroupId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UserId",         (object?)r.UserId         ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@BranchId",       (object?)r.BranchId       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FromDate",       (object?)r.FromDate       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ToDate",         (object?)r.ToDate         ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Prefix",         (object?)r.Prefix         ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@YearFormat",     (object?)r.YearFormat     ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MonthFormat",    (object?)r.MonthFormat    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CounterLength",  r.CounterLength));
        cmd.Parameters.Add(new SqlParameter("@CounterStart",   r.CounterStart));
        cmd.Parameters.Add(new SqlParameter("@ResetPeriod",    (int)r.ResetPeriod));
        cmd.Parameters.Add(new SqlParameter("@TotalLength",    (object?)r.TotalLength    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Weight",         r.Weight));
        cmd.Parameters.Add(new SqlParameter("@IsActive",       r.IsActive));
        cmd.Parameters.Add(new SqlParameter("@CreatedBy",      (object?)r.CreatedBy      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UpdatedBy",      (object?)r.UpdatedBy      ?? DBNull.Value));
    }

    private static DocumentNumberRule Map(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        Name            = r.GetString(1),
        DocumentTypeId  = r.GetInt32(2),
        ContactId       = r.IsDBNull(3) ? null : r.GetInt32(3),
        ContactGroupId  = r.IsDBNull(4) ? null : r.GetInt32(4),
        UserId          = r.IsDBNull(5) ? null : r.GetInt32(5),
        BranchId        = r.IsDBNull(6) ? null : r.GetInt32(6),
        FromDate        = r.IsDBNull(7) ? null : r.GetDateTime(7),
        ToDate          = r.IsDBNull(8) ? null : r.GetDateTime(8),
        Prefix          = r.IsDBNull(9) ? null : r.GetString(9),
        YearFormat      = r.IsDBNull(10) ? null : r.GetString(10),
        MonthFormat     = r.IsDBNull(11) ? null : r.GetString(11),
        CounterLength   = r.GetInt32(12),
        CounterStart    = r.GetInt32(13),
        ResetPeriod     = (DocumentNumberResetPeriod)r.GetInt32(14),
        TotalLength     = r.IsDBNull(15) ? null : r.GetInt32(15),
        Weight          = r.GetInt32(16),
        IsActive        = r.GetBoolean(17),
        CreatedBy       = r.IsDBNull(18) ? null : r.GetString(18),
        Created         = r.GetDateTime(19),
        UpdatedBy       = r.IsDBNull(20) ? null : r.GetString(20),
        Updated         = r.IsDBNull(21) ? null : r.GetDateTime(21),
    };
}
