using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — FormFieldBehavior. Per-company DB (tenant routing); form kodu başına
/// full-replace: DELETE + INSERT tek transaction'da.
/// </summary>
public sealed class SqlFormFieldBehaviorRepository : IFormFieldBehaviorRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlFormFieldBehaviorRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[FormFieldBehavior]";
    }

    public async Task<IReadOnlyCollection<FormFieldBehavior>> GetByFormCodeAsync(string formCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[FormCode],[FieldKey],[IsVisible],[IsRequired],[DefaultValue],
                   [LabelText],[LabelStyle],[RulesJSON],[SortOrder],[CardSection],[CardOrder],[CardWidth],[IsActive],
                   [CreatedById],[CreatedBy],[Created],[UpdatedById],[UpdatedBy],[Updated],
                   -- 2026-08-20 RowHeight SONA eklendi: mevcut ordinal eslemesi (0..19)
                   -- bozulmasin diye araya DEGIL sona. Okuma indeksi 20.
                   [RowHeight],[CellWidthPx],[TargetTabKey],[TargetTab],[Align]
            FROM {_table}
            WHERE [FormCode]=@FormCode AND [IsActive]=1
            ORDER BY [SortOrder],[FieldKey];
            """;
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        var list = new List<FormFieldBehavior>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new FormFieldBehavior
            {
                Id = r.GetInt32(0),
                FormCode = r.GetString(1),
                FieldKey = r.GetString(2),
                IsVisible = r.GetBoolean(3),
                IsRequired = r.GetBoolean(4),
                DefaultValue = r.IsDBNull(5) ? null : r.GetString(5),
                LabelText = r.IsDBNull(6) ? null : r.GetString(6),
                LabelStyle = r.IsDBNull(7) ? null : r.GetString(7),
                RulesJson = r.IsDBNull(8) ? null : r.GetString(8),
                SortOrder = r.GetInt32(9),
                CardSection = r.IsDBNull(10) ? null : r.GetInt32(10),
                CardOrder = r.IsDBNull(11) ? null : r.GetInt32(11),
                CardWidth = r.IsDBNull(12) ? null : r.GetInt32(12),
                IsActive = r.GetBoolean(13),
                CreatedById = r.IsDBNull(14) ? null : r.GetInt32(14),
                CreatedBy = r.IsDBNull(15) ? null : r.GetString(15),
                Created = r.GetDateTime(16),
                UpdatedById = r.IsDBNull(17) ? null : r.GetInt32(17),
                UpdatedBy = r.IsDBNull(18) ? null : r.GetString(18),
                Updated = r.IsDBNull(19) ? null : r.GetDateTime(19),
                RowHeight = r.IsDBNull(20) ? null : r.GetInt32(20),
                CellWidthPx = r.IsDBNull(21) ? null : r.GetInt32(21),
                TargetTabKey = r.IsDBNull(22) ? null : r.GetString(22),
                TargetTab = r.IsDBNull(23) ? null : r.GetString(23),
                Align = r.IsDBNull(24) ? null : r.GetString(24),
            });
        }
        return list;
    }

    public async Task ReplaceForFormAsync(string formCode, IReadOnlyCollection<FormFieldBehavior> rows, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_table} WHERE [FormCode]=@FormCode;";
                del.Parameters.Add(new SqlParameter("@FormCode", formCode));
                await del.ExecuteNonQueryAsync(ct);
            }

            foreach (var row in rows)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_table}
                        ([FormCode],[FieldKey],[IsVisible],[IsRequired],[DefaultValue],
                         [LabelText],[LabelStyle],[RulesJSON],[SortOrder],[CardSection],[CardOrder],[CardWidth],[IsActive],
                         [CreatedById],[CreatedBy],[RowHeight],[CellWidthPx],[TargetTabKey],[TargetTab],[Align])
                    VALUES
                        (@FormCode,@FieldKey,@IsVisible,@IsRequired,@DefaultValue,
                         @LabelText,@LabelStyle,@RulesJson,@SortOrder,@CardSection,@CardOrder,@CardWidth,1,
                         @CreatedById,@CreatedBy,@RowHeight,@CellWidthPx,@TargetTabKey,@TargetTab,@Align);
                    """;
                ins.Parameters.Add(new SqlParameter("@FormCode", formCode));
                ins.Parameters.Add(new SqlParameter("@FieldKey", row.FieldKey));
                ins.Parameters.Add(new SqlParameter("@IsVisible", row.IsVisible));
                ins.Parameters.Add(new SqlParameter("@IsRequired", row.IsRequired));
                ins.Parameters.Add(new SqlParameter("@DefaultValue", (object?)row.DefaultValue ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@LabelText", (object?)row.LabelText ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@LabelStyle", (object?)row.LabelStyle ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@RulesJson", (object?)row.RulesJson ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@SortOrder", row.SortOrder));
                ins.Parameters.Add(new SqlParameter("@CardSection", (object?)row.CardSection ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CardOrder", (object?)row.CardOrder ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CardWidth", (object?)row.CardWidth ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@RowHeight", (object?)row.RowHeight ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CellWidthPx", (object?)row.CellWidthPx ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Align", (object?)row.Align ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@TargetTabKey", (object?)row.TargetTabKey ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@TargetTab", (object?)row.TargetTab ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)row.CreatedById ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CreatedBy", (object?)row.CreatedBy ?? DBNull.Value));
                await ins.ExecuteNonQueryAsync(ct);
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
