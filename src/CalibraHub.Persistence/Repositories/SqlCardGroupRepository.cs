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
        _table        = $"[{schema}].[card_groups]";
        _mappingTable = $"[{schema}].[card_group_mappings]";
    }

    public async Task<IReadOnlyCollection<CardGroup>> GetByLevelAsync(int cardType, int level, int? parentId, CancellationToken ct)
    {
        var list = new List<CardGroup>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();

        if (level == 1)
        {
            command.CommandText = $"""
                SELECT [id], [card_type], [level], [parent_id], [code], [description]
                FROM {_table}
                WHERE [card_type] = @CardType AND [level] = 1
                ORDER BY [code];
                """;
        }
        else if (parentId.HasValue)
        {
            command.CommandText = $"""
                SELECT [id], [card_type], [level], [parent_id], [code], [description]
                FROM {_table}
                WHERE [card_type] = @CardType AND [level] = @Level AND [parent_id] = @ParentId
                ORDER BY [code];
                """;
            command.Parameters.Add(new SqlParameter("@Level", level));
            command.Parameters.Add(new SqlParameter("@ParentId", parentId.Value));
        }
        else
        {
            // Level > 1 but no parent filter — return all at this level
            command.CommandText = $"""
                SELECT [id], [card_type], [level], [parent_id], [code], [description]
                FROM {_table}
                WHERE [card_type] = @CardType AND [level] = @Level
                ORDER BY [code];
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
            SELECT [id], [card_type], [level], [parent_id], [code], [description]
            FROM {_table}
            WHERE [parent_id] = @ParentId
            ORDER BY [code];
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
            SELECT [id], [card_type], [level], [parent_id], [code], [description]
            FROM {_table} WHERE [id] = @Id;
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
        command.CommandText = $"SELECT COUNT(1) FROM {_table} WHERE [parent_id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    public async Task AddAsync(CardGroup group, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_table} ([card_type], [level], [parent_id], [code], [description])
            VALUES (@CardType, @Level, @ParentId, @Code, @Description);
            """;
        command.Parameters.Add(new SqlParameter("@CardType", group.CardType));
        command.Parameters.Add(new SqlParameter("@Level", group.Level));
        command.Parameters.Add(new SqlParameter("@ParentId", (object?)group.ParentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Code", group.Code));
        command.Parameters.Add(new SqlParameter("@Description", (object?)group.Description ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(CardGroup group, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_table}
            SET [code] = @Code, [description] = @Description
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", group.Id));
        command.Parameters.Add(new SqlParameter("@Code", group.Code));
        command.Parameters.Add(new SqlParameter("@Description", (object?)group.Description ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_table} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
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
            SELECT m.[level], g.[id], g.[code], g.[description]
            FROM {_mappingTable} m
            INNER JOIN {_table} g ON g.[id] = m.[card_group_id]
            WHERE m.[entity_type] = @EntityType AND m.[entity_id] = @EntityId
            ORDER BY m.[level];
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
            // Delete existing mappings for levels being updated
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = tx;
            delCmd.CommandText = $"""
                DELETE FROM {_mappingTable}
                WHERE [entity_type] = @EntityType AND [entity_id] = @EntityId;
                """;
            delCmd.Parameters.Add(new SqlParameter("@EntityType", entityType));
            delCmd.Parameters.Add(new SqlParameter("@EntityId", entityId));
            await delCmd.ExecuteNonQueryAsync(ct);

            // Insert new mappings (skip null cardGroupId)
            foreach (var (level, cardGroupId) in levels)
            {
                if (!cardGroupId.HasValue) continue;
                await using var insCmd = connection.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_mappingTable} ([entity_type], [entity_id], [level], [card_group_id])
                    VALUES (@EntityType, @EntityId, @Level, @CardGroupId);
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
