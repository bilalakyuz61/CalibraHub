using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// DocumentSource — Document → Document köprüsü. EnsureSchema runtime'da tabloyu
/// oluşturur (Document.cs ile birlikte deploy edilen bağımsız feature). Şema yoksa
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
        _table = $"[{s}].[DocumentSource]";
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'{_table}', N'U') IS NULL
            BEGIN
                CREATE TABLE {_table} (
                    [Id]               INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DocumentSource] PRIMARY KEY,
                    [DocumentId]       INT NOT NULL,
                    [SourceDocumentId] INT NOT NULL,
                    [Created]          DATETIME NOT NULL DEFAULT GETDATE()
                );
                CREATE UNIQUE INDEX [IX_DocumentSource_Pair]
                    ON {_table} ([DocumentId], [SourceDocumentId]);
                CREATE INDEX [IX_DocumentSource_Src]
                    ON {_table} ([SourceDocumentId]);
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
                           WHERE [DocumentId] = @Doc AND [SourceDocumentId] = @Src)
            BEGIN
                INSERT INTO {_table} ([DocumentId], [SourceDocumentId], [Created])
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
        cmd.CommandText = $"SELECT [SourceDocumentId] FROM {_table} WHERE [DocumentId] = @Doc ORDER BY [Id];";
        cmd.Parameters.Add(new SqlParameter("@Doc", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetInt32(0));
        return list;
    }

    public async Task<bool> IsSourceConsumedAsync(int sourceDocumentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 1 FROM {_table} WHERE [SourceDocumentId] = @Src;";
        cmd.Parameters.Add(new SqlParameter("@Src", sourceDocumentId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value;
    }

    public async Task<IReadOnlyCollection<int>> GetDerivedDocumentIdsAsync(int sourceDocumentId, CancellationToken ct)
    {
        // Tablo henuz olusturulmamissa bos liste don (EnsureSchema cagrisi yapilmadan erken cagrim).
        var checkSql = $"SELECT OBJECT_ID(N'{_table}', N'U');";
        var list = new List<int>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using (var chkCmd = conn.CreateCommand())
        {
            chkCmd.CommandText = checkSql;
            var exists = await chkCmd.ExecuteScalarAsync(ct);
            if (exists == null || exists == DBNull.Value) return list;
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [DocumentId] FROM {_table} WHERE [SourceDocumentId] = @Src ORDER BY [Id];";
        cmd.Parameters.Add(new SqlParameter("@Src", sourceDocumentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetInt32(0));
        return list;
    }
}
