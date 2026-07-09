using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Lot (parti) ana kayıtları — get-or-create + stok takip tipi okuma.
/// UX_Lot_Item_LotNo unique index'i yarışı DB seviyesinde çözer: eşzamanlı iki
/// oluşturma denemesinden kaybeden 2601/2627 alır ve mevcut kaydı yeniden okur.
/// </summary>
public sealed class SqlLotRepository : ILotRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlLotRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    public async Task<string?> GetItemTrackingTypeAsync(int itemId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT ISNULL([TrackingType], 'None') FROM {T("Items")} WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", itemId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<int> GetOrCreateAsync(int itemId, string lotNo, int? createdById, CancellationToken ct)
    {
        var trimmed = lotNo.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Lot no boş olamaz.", nameof(lotNo));

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var existing = await FindAsync(conn, itemId, trimmed, ct);
        if (existing.HasValue) return existing.Value;

        try
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = $"""
                INSERT INTO {T("Lot")} ([ItemId],[LotNo],[CreatedById])
                VALUES (@ItemId, @LotNo, @CreatedById);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            ins.Parameters.AddWithValue("@ItemId", itemId);
            ins.Parameters.AddWithValue("@LotNo", trimmed);
            ins.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
            return Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Yarışı kaybeden taraf: kayıt az önce başka bağlantıca oluşturuldu — yeniden oku.
            return await FindAsync(conn, itemId, trimmed, ct)
                ?? throw new InvalidOperationException($"Lot çözümlenemedi: '{trimmed}'.");
        }
    }

    private async Task<int?> FindAsync(SqlConnection conn, int itemId, string lotNo, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 [Id] FROM {T("Lot")} WHERE [ItemId] = @ItemId AND [LotNo] = @LotNo;";
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@LotNo", lotNo);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is int id ? id : null;
    }
}
