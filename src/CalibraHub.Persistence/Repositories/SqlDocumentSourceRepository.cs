using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// document_source — Document → Document koprusu. EnsureSchema runtime'da tabloyu
/// olusturur (Document.cs ile birlikte deploy edilen bagimsiz feature). Sema yoksa
/// otomatik kurulum, varsa idempotent.
/// </summary>
public sealed class SqlDocumentSourceRepository : IDocumentSourceRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlDocumentSourceRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[document_source]";
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'{_table}', N'U') IS NULL
            BEGIN
                CREATE TABLE {_table} (
                    [id]                  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [document_id]         INT NOT NULL,
                    [source_document_id]  INT NOT NULL,
                    [created_at]          DATETIME2 NOT NULL DEFAULT GETDATE()
                );
                CREATE UNIQUE INDEX [IX_document_source_pair]
                    ON {_table} ([document_id], [source_document_id]);
                CREATE INDEX [IX_document_source_src]
                    ON {_table} ([source_document_id]);
            END;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddAsync(int documentId, int sourceDocumentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF NOT EXISTS (SELECT 1 FROM {_table}
                           WHERE [document_id] = @Doc AND [source_document_id] = @Src)
            BEGIN
                INSERT INTO {_table} ([document_id], [source_document_id], [created_at])
                VALUES (@Doc, @Src, GETDATE());
            END;
            """;
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        cmd.Parameters.Add(new SqlParameter("@Src", sourceDocumentId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<int>> GetSourceIdsAsync(int documentId, CancellationToken ct)
    {
        var list = new List<int>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [source_document_id] FROM {_table} WHERE [document_id] = @Doc ORDER BY [id];";
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetInt32(0));
        return list;
    }

    public async Task<bool> IsSourceConsumedAsync(int sourceDocumentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 1 FROM {_table} WHERE [source_document_id] = @Src;";
        cmd.Parameters.Add(new SqlParameter("@Src", sourceDocumentId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value;
    }
}
