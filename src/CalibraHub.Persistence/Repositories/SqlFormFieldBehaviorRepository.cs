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
                   [CreatedById],[CreatedBy],[Created],[UpdatedById],[UpdatedBy],[Updated]
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
                IsActive = r.GetBoolean(12),
                CreatedById = r.IsDBNull(13) ? null : r.GetInt32(13),
                CreatedBy = r.IsDBNull(14) ? null : r.GetString(14),
                Created = r.GetDateTime(15),
                UpdatedById = r.IsDBNull(16) ? null : r.GetInt32(16),
                UpdatedBy = r.IsDBNull(17) ? null : r.GetString(17),
                Updated = r.IsDBNull(18) ? null : r.GetDateTime(18),
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
                         [LabelText],[LabelStyle],[RulesJSON],[SortOrder],[CardSection],[CardOrder],[IsActive],
                         [CreatedById],[CreatedBy])
                    VALUES
                        (@FormCode,@FieldKey,@IsVisible,@IsRequired,@DefaultValue,
                         @LabelText,@LabelStyle,@RulesJson,@SortOrder,@CardSection,@CardOrder,1,
                         @CreatedById,@CreatedBy);
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
