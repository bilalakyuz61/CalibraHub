using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Toplu mail batch + item ADO.NET repository. Per-company DB — schema parametre.
/// Item-write yuksek hacimli olabilir; ileride bulk insert'e geciş kolay.
/// </summary>
public sealed class SqlMailSendBatchRepository : IMailSendBatchRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _batchTable;
    private readonly string _itemTable;

    public SqlMailSendBatchRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _batchTable = $"[{s}].[MailSendBatch]";
        _itemTable  = $"[{s}].[MailSendLogItem]";
    }

    public async Task<int> CreateBatchAsync(MailSendBatch batch, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_batchTable}
                ([LayoutId],[LayoutName],[Subject],[BodyPreview],[TitleIdsJson],[TitleNamesJson],
                 [TotalCount],[SentCount],[FailCount],[SentBy],[SentAt],[CompanyId])
            VALUES
                (@LayoutId,@LayoutName,@Subject,@BodyPreview,@TitleIdsJson,@TitleNamesJson,
                 @TotalCount,@SentCount,@FailCount,@SentBy,SYSUTCDATETIME(),@CompanyId);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@LayoutId",       batch.LayoutId));
        cmd.Parameters.Add(new SqlParameter("@LayoutName",     (object?)batch.LayoutName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Subject",        (object?)batch.Subject ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@BodyPreview",    (object?)batch.BodyPreview ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TitleIdsJson",   (object?)batch.TitleIdsJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TitleNamesJson", (object?)batch.TitleNamesJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TotalCount",     batch.TotalCount));
        cmd.Parameters.Add(new SqlParameter("@SentCount",      batch.SentCount));
        cmd.Parameters.Add(new SqlParameter("@FailCount",      batch.FailCount));
        cmd.Parameters.Add(new SqlParameter("@SentBy",         (object?)batch.SentBy ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CompanyId",      batch.CompanyId));
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<int> AddItemAsync(MailSendLogItem item, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_itemTable}
                ([BatchId],[ContactPersonId],[RecipientName],[RecipientEmail],
                 [TitleName],[ContactName],[Status],[ErrorMessage],[SentAt])
            VALUES
                (@BatchId,@ContactPersonId,@RecipientName,@RecipientEmail,
                 @TitleName,@ContactName,@Status,@ErrorMessage,@SentAt);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@BatchId",         item.BatchId));
        cmd.Parameters.Add(new SqlParameter("@ContactPersonId", (object?)item.ContactPersonId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RecipientName",   (object?)item.RecipientName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RecipientEmail",  item.RecipientEmail ?? string.Empty));
        cmd.Parameters.Add(new SqlParameter("@TitleName",       (object?)item.TitleName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactName",     (object?)item.ContactName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Status",          item.Status ?? "Queued"));
        cmd.Parameters.Add(new SqlParameter("@ErrorMessage",    (object?)item.ErrorMessage ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SentAt",          (object?)item.SentAt ?? DBNull.Value));
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateItemStatusAsync(int itemId, string status, string? errorMessage, DateTime? sentAt, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_itemTable}
            SET [Status]       = @Status,
                [ErrorMessage] = @ErrorMessage,
                [SentAt]       = @SentAt
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",           itemId));
        cmd.Parameters.Add(new SqlParameter("@Status",       status ?? "Queued"));
        cmd.Parameters.Add(new SqlParameter("@ErrorMessage", (object?)errorMessage ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SentAt",       (object?)sentAt ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateBatchCountsAsync(int batchId, int sentCount, int failCount, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_batchTable}
            SET [SentCount] = @Sent,
                [FailCount] = @Fail
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",   batchId));
        cmd.Parameters.Add(new SqlParameter("@Sent", sentCount));
        cmd.Parameters.Add(new SqlParameter("@Fail", failCount));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<MailSendBatch>> GetRecentBatchesAsync(int companyId, int take, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Take)
                [Id],[LayoutId],[LayoutName],[Subject],[BodyPreview],
                [TitleIdsJson],[TitleNamesJson],
                [TotalCount],[SentCount],[FailCount],
                [SentBy],[SentAt],[CompanyId]
            FROM {_batchTable}
            WHERE (@CompanyId = 0 OR [CompanyId] = @CompanyId)
            ORDER BY [SentAt] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Take",      take <= 0 ? 100 : take));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        var list = new List<MailSendBatch>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapBatch(r));
        return list;
    }

    public async Task<(MailSendBatch? Batch, IReadOnlyList<MailSendLogItem> Items)> GetBatchDetailAsync(
        int batchId, int companyId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        MailSendBatch? batch = null;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT TOP 1
                    [Id],[LayoutId],[LayoutName],[Subject],[BodyPreview],
                    [TitleIdsJson],[TitleNamesJson],
                    [TotalCount],[SentCount],[FailCount],
                    [SentBy],[SentAt],[CompanyId]
                FROM {_batchTable}
                WHERE [Id] = @Id AND (@CompanyId = 0 OR [CompanyId] = @CompanyId);
                """;
            cmd.Parameters.Add(new SqlParameter("@Id",        batchId));
            cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
                batch = MapBatch(r);
        }
        if (batch == null) return (null, Array.Empty<MailSendLogItem>());

        var items = new List<MailSendLogItem>();
        await using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = $"""
                SELECT [Id],[BatchId],[ContactPersonId],[RecipientName],[RecipientEmail],
                       [TitleName],[ContactName],[Status],[ErrorMessage],[SentAt]
                FROM {_itemTable}
                WHERE [BatchId] = @Id
                ORDER BY [Id];
                """;
            cmd2.Parameters.Add(new SqlParameter("@Id", batchId));
            await using var r2 = await cmd2.ExecuteReaderAsync(ct);
            while (await r2.ReadAsync(ct))
                items.Add(MapItem(r2));
        }
        return (batch, items);
    }

    private static MailSendBatch MapBatch(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        LayoutId       = r.GetInt32(1),
        LayoutName     = r.IsDBNull(2) ? null : r.GetString(2),
        Subject        = r.IsDBNull(3) ? null : r.GetString(3),
        BodyPreview    = r.IsDBNull(4) ? null : r.GetString(4),
        TitleIdsJson   = r.IsDBNull(5) ? null : r.GetString(5),
        TitleNamesJson = r.IsDBNull(6) ? null : r.GetString(6),
        TotalCount     = r.GetInt32(7),
        SentCount      = r.GetInt32(8),
        FailCount      = r.GetInt32(9),
        SentBy         = r.IsDBNull(10) ? null : r.GetString(10),
        SentAt         = r.GetDateTime(11),
        CompanyId      = r.GetInt32(12),
    };

    private static MailSendLogItem MapItem(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        BatchId         = r.GetInt32(1),
        ContactPersonId = r.IsDBNull(2) ? null : r.GetInt32(2),
        RecipientName   = r.IsDBNull(3) ? null : r.GetString(3),
        RecipientEmail  = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        TitleName       = r.IsDBNull(5) ? null : r.GetString(5),
        ContactName     = r.IsDBNull(6) ? null : r.GetString(6),
        Status          = r.IsDBNull(7) ? "Queued" : r.GetString(7),
        ErrorMessage    = r.IsDBNull(8) ? null : r.GetString(8),
        SentAt          = r.IsDBNull(9) ? null : r.GetDateTime(9),
    };

    public async Task DeleteBatchAsync(int batchId, int companyId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        // Item satirlari + batch baslik tek transaction'da silinir.
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {_itemTable} WHERE [BatchId] = @Id;";
                cmd.Parameters.Add(new SqlParameter("@Id", batchId));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {_batchTable} WHERE [Id] = @Id AND (@CompanyId = 0 OR [CompanyId] = @CompanyId);";
                cmd.Parameters.Add(new SqlParameter("@Id",        batchId));
                cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
