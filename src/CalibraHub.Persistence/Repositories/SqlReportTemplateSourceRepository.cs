using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlReportTemplateSourceRepository : IReportTemplateSourceRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlReportTemplateSourceRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[report_template_sources]";
    }

    private const string Columns =
        "[id],[template_id],[source_name],[view_name],[key_column],[parent_source_name],[parent_key_column],[is_primary],[display_order],[created_at],[sort_column],[sort_direction]";

    public async Task<IReadOnlyList<ReportTemplateSource>> GetByTemplateIdAsync(int templateId, CancellationToken cancellationToken)
    {
        var list = new List<ReportTemplateSource>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM {_table} WHERE [template_id] = @Tid ORDER BY [display_order], [id];";
        cmd.Parameters.Add(new SqlParameter("@Tid", templateId));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(Map(r));
        }
        return list;
    }

    public async Task ReplaceAllAsync(int templateId, IReadOnlyList<ReportTemplateSource> sources, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // 1) Mevcut source'lari sil
            await using (var del = connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_table} WHERE [template_id] = @Tid;";
                del.Parameters.Add(new SqlParameter("@Tid", templateId));
                await del.ExecuteNonQueryAsync(cancellationToken);
            }

            // 2) Yeni source'lari INSERT et
            int order = 0;
            foreach (var s in sources)
            {
                await using var ins = connection.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_table}
                        ([template_id],[source_name],[view_name],[key_column],
                         [parent_source_name],[parent_key_column],[is_primary],[display_order],
                         [sort_column],[sort_direction],[created_at])
                    VALUES (@Tid,@SourceName,@ViewName,@KeyColumn,
                            @ParentSrc,@ParentKey,@IsPrimary,@Order,
                            @SortCol,@SortDir,GETDATE());
                    """;
                ins.Parameters.Add(new SqlParameter("@Tid",        templateId));
                ins.Parameters.Add(new SqlParameter("@SourceName", s.SourceName));
                ins.Parameters.Add(new SqlParameter("@ViewName",   s.ViewName));
                ins.Parameters.Add(new SqlParameter("@KeyColumn",  s.KeyColumn));
                ins.Parameters.Add(new SqlParameter("@ParentSrc",  (object?)s.ParentSourceName ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@ParentKey",  (object?)s.ParentKeyColumn  ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@IsPrimary",  s.IsPrimary));
                ins.Parameters.Add(new SqlParameter("@Order",      s.DisplayOrder > 0 ? s.DisplayOrder : order));
                ins.Parameters.Add(new SqlParameter("@SortCol",
                    string.IsNullOrWhiteSpace(s.SortColumn) ? (object)DBNull.Value : s.SortColumn.Trim()));
                ins.Parameters.Add(new SqlParameter("@SortDir",
                    string.IsNullOrWhiteSpace(s.SortDirection) ? (object)DBNull.Value : s.SortDirection.Trim().ToUpperInvariant()));
                await ins.ExecuteNonQueryAsync(cancellationToken);
                order++;
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { /* swallow */ }
            throw;
        }
    }

    public async Task DeleteByTemplateIdAsync(int templateId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [template_id] = @Tid;";
        cmd.Parameters.Add(new SqlParameter("@Tid", templateId));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ReportTemplateSource Map(SqlDataReader r) => new()
    {
        Id                = r.GetInt32(0),
        TemplateId        = r.GetInt32(1),
        SourceName        = r.GetString(2),
        ViewName          = r.GetString(3),
        KeyColumn         = r.GetString(4),
        ParentSourceName  = r.IsDBNull(5) ? null : r.GetString(5),
        ParentKeyColumn   = r.IsDBNull(6) ? null : r.GetString(6),
        IsPrimary         = r.GetBoolean(7),
        DisplayOrder      = r.GetInt32(8),
        CreatedAt         = r.GetDateTime(9),
        SortColumn        = r.IsDBNull(10) ? null : r.GetString(10),
        SortDirection     = r.IsDBNull(11) ? null : r.GetString(11),
    };
}
