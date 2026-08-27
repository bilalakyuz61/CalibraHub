using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlCardGroupRepository : ICardGroupRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _mappingTable;

    public SqlCardGroupRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table        = $"[{schema}].[CardGroup]";
        _mappingTable = $"[{schema}].[CardGroupMapping]";
    }

    public async Task<IReadOnlyCollection<CardGroup>> GetByLevelAsync(int cardType, int level, int? parentId, CancellationToken ct)
    {
        var list = new List<CardGroup>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();

        if (level == 1)
        {
            command.CommandText = $"""
                SELECT [Id], [CardType], [Level], [ParentId], [Code], [Description]
                FROM {_table}
                WHERE [CardType] = @CardType AND [Level] = 1
                ORDER BY [Code];
                """;
        }
        else if (parentId.HasValue)
        {
            command.CommandText = $"""
                SELECT [Id], [CardType], [Level], [ParentId], [Code], [Description]
                FROM {_table}
                WHERE [CardType] = @CardType AND [Level] = @Level AND [ParentId] = @ParentId
                ORDER BY [Code];
                """;
            command.Parameters.Add(new SqlParameter("@Level", level));
            command.Parameters.Add(new SqlParameter("@ParentId", parentId.Value));
        }
        else
        {
            // Level > 1 but no parent filter — return all at this level
            command.CommandText = $"""
                SELECT [Id], [CardType], [Level], [ParentId], [Code], [Description]
                FROM {_table}
                WHERE [CardType] = @CardType AND [Level] = @Level
                ORDER BY [Code];
                """;
            command.Parameters.Add(new SqlParameter("@Level", level));
        }

        command.Parameters.Add(new SqlParameter("@CardType", cardType));

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));

        return list;
    }

    public async Task<IReadOnlyCollection<CardGroup>> GetByParentAsync(int parentId, CancellationToken ct)
    {
        var list = new List<CardGroup>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CardType], [Level], [ParentId], [Code], [Description]
            FROM {_table}
            WHERE [ParentId] = @ParentId
            ORDER BY [Code];
            """;
        command.Parameters.Add(new SqlParameter("@ParentId", parentId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    public async Task<CardGroup?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CardType], [Level], [ParentId], [Code], [Description]
            FROM {_table} WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Map(reader);
    }

    public async Task<bool> HasChildrenAsync(int id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {_table} WHERE [ParentId] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    public async Task<int> AddAsync(CardGroup group, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_table} ([CardType], [Level], [ParentId], [Code], [Description], [CompanyId])
            VALUES (@CardType, @Level, @ParentId, @Code, @Description, @CompanyId);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        command.Parameters.Add(new SqlParameter("@CardType", group.CardType));
        command.Parameters.Add(new SqlParameter("@Level", group.Level));
        command.Parameters.Add(new SqlParameter("@ParentId", (object?)group.ParentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Code", group.Code));
        command.Parameters.Add(new SqlParameter("@Description", (object?)group.Description ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(CardGroup group, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_table}
            SET [Code] = @Code, [Description] = @Description
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@Id", group.Id));
        command.Parameters.Add(new SqlParameter("@Code", group.Code));
        command.Parameters.Add(new SqlParameter("@Description", (object?)group.Description ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        // 2026-08-26: grup silinirken CardGroupMapping satirlari daha once temizlenmiyordu
        // (bu yuzden FK_CardGroupMapping_CardGroup bilinclli olarak eklenmedi — bkz. rapor
        // Seq 1118 istege bagli madde 5). Once bagli mapping'leri sil, sonra grubu sil —
        // ayni baglantida iki komut (transaction gerekmeyecek kadar basit, DB atomikligi
        // yeterli: ikinci komut basarisiz olsa da yalnizca oksuz mapping kalmaz, grup kalir).
        await using (var mappingCmd = connection.CreateCommand())
        {
            mappingCmd.CommandText = $"""
                DELETE FROM {_mappingTable} WHERE [CardGroupId] = @Id
                  AND EXISTS (SELECT 1 FROM {_table} g WHERE g.[Id] = @Id AND g.[CompanyId] = @CompanyId);
                """;
            mappingCmd.Parameters.Add(new SqlParameter("@Id", id));
            mappingCmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await mappingCmd.ExecuteNonQueryAsync(ct);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static CardGroup Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CardType = r.GetByte(1),
        Level = r.GetByte(2),
        ParentId = r.IsDBNull(3) ? null : r.GetInt32(3),
        Code = r.GetString(4),
        Description = r.IsDBNull(5) ? null : r.GetString(5)
    };

    // ── Entity group mappings ──────────────────────────────────────────────

    public async Task<IReadOnlyCollection<CardGroupMappingRow>> GetEntityMappingsAsync(
        int entityType, string entityId, CancellationToken ct)
    {
        var list = new List<CardGroupMappingRow>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.[Level], g.[Id], g.[Code], g.[Description]
            FROM {_mappingTable} m
            INNER JOIN {_table} g ON g.[Id] = m.[CardGroupId]
            WHERE m.[EntityType] = @EntityType AND m.[EntityId] = @EntityId
            ORDER BY m.[Level];
            """;
        command.Parameters.Add(new SqlParameter("@EntityType", entityType));
        command.Parameters.Add(new SqlParameter("@EntityId", entityId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new CardGroupMappingRow(
                reader.GetByte(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return list;
    }

    public async Task SaveEntityMappingsAsync(
        int entityType, string entityId,
        IReadOnlyCollection<(int Level, int? CardGroupId)> levels,
        CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            var companyId = _connectionFactory.ResolveEffectiveCompanyId();

            // Delete existing mappings for levels being updated
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = tx;
            delCmd.CommandText = $"""
                DELETE FROM {_mappingTable}
                WHERE [EntityType] = @EntityType AND [EntityId] = @EntityId AND [CompanyId] = @CompanyId;
                """;
            delCmd.Parameters.Add(new SqlParameter("@EntityType", entityType));
            delCmd.Parameters.Add(new SqlParameter("@EntityId", entityId));
            delCmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await delCmd.ExecuteNonQueryAsync(ct);

            // Insert new mappings (skip null cardGroupId)
            foreach (var (level, cardGroupId) in levels)
            {
                if (!cardGroupId.HasValue) continue;
                await using var insCmd = connection.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_mappingTable} ([EntityType], [EntityId], [Level], [CardGroupId], [CompanyId])
                    VALUES (@EntityType, @EntityId, @Level, @CardGroupId,
                        (SELECT g.[CompanyId] FROM {_table} g WHERE g.[Id] = @CardGroupId));
                    """;
                insCmd.Parameters.Add(new SqlParameter("@EntityType", entityType));
                insCmd.Parameters.Add(new SqlParameter("@EntityId", entityId));
                insCmd.Parameters.Add(new SqlParameter("@Level", (byte)level));
                insCmd.Parameters.Add(new SqlParameter("@CardGroupId", cardGroupId.Value));
                await insCmd.ExecuteNonQueryAsync(ct);
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
