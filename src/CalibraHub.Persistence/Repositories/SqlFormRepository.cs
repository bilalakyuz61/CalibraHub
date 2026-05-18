using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// IFormRepository — raw ADO.NET implementasyonu.
/// dbo.Forms tablosuna SELECT / INSERT / UPDATE / DELETE işlemleri.
/// </summary>
public sealed class SqlFormRepository : IFormRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlFormRepository(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyCollection<FormDto>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FormCode, FormName, Module, SubModule, SortOrder, IsActive,
                   BaseTable, BaseRecordKey
              FROM dbo.Forms
             ORDER BY SortOrder, FormCode
            """;

        var list = new List<FormDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(MapRow(reader));
        }
        return list;
    }

    public async Task<FormDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FormCode, FormName, Module, SubModule, SortOrder, IsActive,
                   BaseTable, BaseRecordKey
              FROM dbo.Forms
             WHERE Id = @Id
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return MapRow(reader);
        return null;
    }

    public async Task<FormDto?> GetByCodeAsync(string formCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode)) return null;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FormCode, FormName, Module, SubModule, SortOrder, IsActive,
                   BaseTable, BaseRecordKey
              FROM dbo.Forms
             WHERE FormCode = @FormCode
            """;
        // FormCode UNIQUE — case-insensitive SQL collation varsayilani altinda eslesir.
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode.Trim()));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return MapRow(reader);
        return null;
    }

    public async Task<int> CreateAsync(CreateFormRequest request, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.Forms
                (FormCode, FormName, Module, SubModule, SortOrder, IsActive,
                 BaseTable, BaseRecordKey)
            VALUES
                (@FormCode, @FormName, @Module, @SubModule, @SortOrder, @IsActive,
                 @BaseTable, @BaseRecordKey);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        cmd.Parameters.Add(new SqlParameter("@FormCode", request.FormCode));
        cmd.Parameters.Add(new SqlParameter("@FormName", request.FormName));
        cmd.Parameters.Add(new SqlParameter("@Module", (object?)request.Module ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SubModule", (object?)request.SubModule ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", request.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive", request.IsActive));
        cmd.Parameters.Add(new SqlParameter("@BaseTable", (object?)request.BaseTable ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@BaseRecordKey", (object?)request.BaseRecordKey ?? DBNull.Value));

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int newId ? newId : Convert.ToInt32(result);
    }

    public async Task UpdateAsync(UpdateFormRequest request, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE dbo.Forms
               SET FormCode      = @FormCode,
                   FormName      = @FormName,
                   Module        = @Module,
                   SubModule     = @SubModule,
                   SortOrder     = @SortOrder,
                   IsActive      = @IsActive,
                   BaseTable     = @BaseTable,
                   BaseRecordKey = @BaseRecordKey
             WHERE Id = @Id
            """;

        cmd.Parameters.Add(new SqlParameter("@Id", request.Id));
        cmd.Parameters.Add(new SqlParameter("@FormCode", request.FormCode));
        cmd.Parameters.Add(new SqlParameter("@FormName", request.FormName));
        cmd.Parameters.Add(new SqlParameter("@Module", (object?)request.Module ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SubModule", (object?)request.SubModule ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", request.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive", request.IsActive));
        cmd.Parameters.Add(new SqlParameter("@BaseTable", (object?)request.BaseTable ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@BaseRecordKey", (object?)request.BaseRecordKey ?? DBNull.Value));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.Forms WHERE Id = @Id";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Yardımcı ───────────────────────────────────────────────────────────
    private static FormDto MapRow(SqlDataReader reader)
    {
        return new FormDto(
            Id: reader.GetInt32(reader.GetOrdinal("Id")),
            FormCode: reader.GetString(reader.GetOrdinal("FormCode")),
            FormName: reader.GetString(reader.GetOrdinal("FormName")),
            Module: reader.IsDBNull(reader.GetOrdinal("Module")) ? null : reader.GetString(reader.GetOrdinal("Module")),
            SubModule: reader.IsDBNull(reader.GetOrdinal("SubModule")) ? null : reader.GetString(reader.GetOrdinal("SubModule")),
            SortOrder: reader.GetInt32(reader.GetOrdinal("SortOrder")),
            IsActive: reader.GetBoolean(reader.GetOrdinal("IsActive")),
            BaseTable: reader.IsDBNull(reader.GetOrdinal("BaseTable")) ? null : reader.GetString(reader.GetOrdinal("BaseTable")),
            BaseRecordKey: reader.IsDBNull(reader.GetOrdinal("BaseRecordKey")) ? null : reader.GetString(reader.GetOrdinal("BaseRecordKey"))
        );
    }
}
