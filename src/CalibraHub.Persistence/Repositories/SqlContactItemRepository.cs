using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// ContactItem (cari × stok eslestirmesi) icin ADO.NET repository.
/// SqlAddressRepository ile ayni pattern: schema-aware, SqlServerConnectionFactory injection.
/// Liste sorgusu Items ile JOIN edip stok kod/adini ayni satirda doner.
/// UNIQUE INDEX ihlalleri (2601/2627) anlamli InvalidOperationException olarak yukseltilir.
/// </summary>
public sealed class SqlContactItemRepository : IContactItemRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;
    private readonly string _itemsTable;

    public SqlContactItemRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema = schema.Replace("]", "]]");
        _table = $"[{_schema}].[ContactItem]";
        _itemsTable = $"[{_schema}].[Items]";
    }

    public async Task<IReadOnlyCollection<ContactItemListRow>> GetByContactAsync(int contactId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT ci.[Id], ci.[ContactId], ci.[ItemId],
                   i.[Code]  AS ItemCode,
                   i.[Name]  AS ItemName,
                   ci.[VendorCode], ci.[VendorName], ci.[Notes],
                   ci.[IsActive], ci.[Created], ci.[Updated]
            FROM {_table} ci
            INNER JOIN {_itemsTable} i ON i.[Id] = ci.[ItemId]
            WHERE ci.[ContactId] = @ContactId
            ORDER BY i.[Code];
            """;
        cmd.Parameters.Add(new SqlParameter("@ContactId", contactId));

        var list = new List<ContactItemListRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ContactItemListRow(
                Id:         r.GetInt32(0),
                ContactId:  r.GetInt32(1),
                ItemId:     r.GetInt32(2),
                ItemCode:   r.GetString(3),
                ItemName:   r.GetString(4),
                VendorCode: r.IsDBNull(5) ? null : r.GetString(5),
                VendorName: r.IsDBNull(6) ? null : r.GetString(6),
                Notes:      r.IsDBNull(7) ? null : r.GetString(7),
                IsActive:   r.GetBoolean(8),
                CreatedAt:  r.GetDateTime(9),
                UpdatedAt:  r.IsDBNull(10) ? null : r.GetDateTime(10)));
        }
        return list;
    }

    public async Task<int> AddAsync(ContactItem entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
                ([ContactId],[ItemId],[VendorCode],[VendorName],[Notes],[IsActive],[Created])
            VALUES
                (@ContactId,@ItemId,@VendorCode,@VendorName,@Notes,@IsActive,@CreatedAt);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        AddParams(cmd, entity);
        cmd.Parameters.Add(new SqlParameter("@CreatedAt",
            entity.CreatedAt == default ? DateTime.UtcNow : entity.CreatedAt));

        try
        {
            return (int)(await cmd.ExecuteScalarAsync(ct))!;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new InvalidOperationException("Bu stok zaten bu cariye eslestirilmis.", ex);
        }
    }

    public async Task UpdateAsync(ContactItem entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET [ItemId]     = @ItemId,
                [VendorCode] = @VendorCode,
                [VendorName] = @VendorName,
                [Notes]      = @Notes,
                [IsActive]   = @IsActive,
                [Updated]    = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;
        AddParams(cmd, entity);
        cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));

        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new InvalidOperationException("Bu stok zaten bu cariye eslestirilmis.", ex);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParams(SqlCommand cmd, ContactItem e)
    {
        cmd.Parameters.Add(new SqlParameter("@ContactId",  e.ContactId));
        cmd.Parameters.Add(new SqlParameter("@ItemId",     e.ItemId));
        cmd.Parameters.Add(new SqlParameter("@VendorCode", (object?)e.VendorCode ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@VendorName", (object?)e.VendorName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Notes",      (object?)e.Notes      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive",   e.IsActive));
    }
}
