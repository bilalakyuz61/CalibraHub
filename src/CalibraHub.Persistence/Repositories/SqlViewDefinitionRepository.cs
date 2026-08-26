using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// ViewDefinition / ViewDefinitionRevision — salt-okunur CRUD (bkz. IViewDefinitionRepository
/// doc-comment'i; yazma işlemleri kasıtlı olarak burada değil, ViewBuilderService'in tek
/// transaction'ı içinde yapılır).
/// </summary>
public sealed class SqlViewDefinitionRepository : IViewDefinitionRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _s;

    private const string SelectColumns =
        "[Id],[ViewName],[Kind],[DefinitionJson],[GeneratedSql],[IsRawMode],[IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated]";

    public SqlViewDefinitionRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _s = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<IReadOnlyList<ViewDefinition>> ListActiveAsync(CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM   [{_s}].[ViewDefinition]
            WHERE  [IsActive] = 1
            ORDER  BY [ViewName];
            """;
        var list = new List<ViewDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapRow(reader));
        return list;
    }

    public async Task<ViewDefinition?> GetByViewNameAsync(string viewName, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM   [{_s}].[ViewDefinition]
            WHERE  [ViewName] = @ViewName AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@ViewName", viewName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<ViewDefinition?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM   [{_s}].[ViewDefinition]
            WHERE  [Id] = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<ViewDefinitionRevision?> GetLatestRevisionAsync(int viewDefinitionId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[ViewDefinitionId],[DefinitionJson],[GeneratedSql],[IsRawMode],[CreatedBy],[Created]
            FROM   [{_s}].[ViewDefinitionRevision]
            WHERE  [ViewDefinitionId] = @Vid
            ORDER  BY [Id] DESC;
            """;
        cmd.Parameters.AddWithValue("@Vid", viewDefinitionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ViewDefinitionRevision
        {
            Id = reader.GetInt32(0),
            ViewDefinitionId = reader.GetInt32(1),
            DefinitionJson = reader.IsDBNull(2) ? null : reader.GetString(2),
            GeneratedSql = reader.GetString(3),
            IsRawMode = reader.GetBoolean(4),
            CreatedBy = reader.IsDBNull(5) ? null : reader.GetString(5),
            Created = reader.GetDateTime(6),
        };
    }

    public async Task<int> CountRevisionsAsync(int viewDefinitionId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM [{_s}].[ViewDefinitionRevision] WHERE [ViewDefinitionId] = @Vid;";
        cmd.Parameters.AddWithValue("@Vid", viewDefinitionId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null ? 0 : Convert.ToInt32(result);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountRevisionsGroupedAsync(CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [ViewDefinitionId], COUNT(1)
            FROM   [{_s}].[ViewDefinitionRevision]
            GROUP  BY [ViewDefinitionId];
            """;
        var dict = new Dictionary<int, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetInt32(0)] = reader.GetInt32(1);
        return dict;
    }

    private static ViewDefinition MapRow(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        ViewName       = r.GetString(1),
        Kind           = Enum.TryParse<ViewDefinitionKind>(r.GetString(2), ignoreCase: true, out var k) ? k : ViewDefinitionKind.Custom,
        DefinitionJson = r.IsDBNull(3) ? null : r.GetString(3),
        GeneratedSql   = r.GetString(4),
        IsRawMode      = r.GetBoolean(5),
        IsActive       = r.GetBoolean(6),
        CreatedBy      = r.IsDBNull(7) ? null : r.GetString(7),
        Created        = r.GetDateTime(8),
        UpdatedBy      = r.IsDBNull(9) ? null : r.GetString(9),
        Updated        = r.IsDBNull(10) ? null : r.GetDateTime(10),
    };
}
