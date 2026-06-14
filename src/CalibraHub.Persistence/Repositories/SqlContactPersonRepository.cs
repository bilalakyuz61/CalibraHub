using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// ContactPerson (cariye bagli iletisim kisileri) ADO.NET repository.
/// Pattern: SqlContactItemRepository. Schema CalibraDatabaseOptions.Schema'dan alinir.
/// Soft delete: IsActive=0 set edilir (kayitlar tarihce icin korunur).
/// 2026-05-31: Legacy 'Title' string kolonu kaldirildi. Unvan artik sadece TitleId
/// (FK -> ContactPersonTitle) uzerinden, gosterim icin JOIN ile TitleName turetilir.
/// CLAUDE.md ID-tabanli eslestirme kuralina tam uyumlu.
/// </summary>
public sealed class SqlContactPersonRepository : IContactPersonRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;
    private readonly string _titleTable;

    public SqlContactPersonRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema = schema.Replace("]", "]]");
        _table = $"[{_schema}].[ContactPerson]";
        _titleTable = $"[{_schema}].[ContactPersonTitle]";
    }

    private string SelectColumns => $"""
        p.[Id],p.[ContactId],p.[FullName],p.[Phone],p.[Email],p.[Notes],
        p.[IsPrimary],p.[IsActive],p.[Created],p.[Updated],p.[CreatedById],p.[UpdatedById],
        p.[TitleId], t.[Name] AS TitleName
        """;

    public async Task<IReadOnlyList<ContactPerson>> GetByContactIdAsync(int contactId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM {_table} p
            LEFT JOIN {_titleTable} t ON t.[Id] = p.[TitleId]
            WHERE p.[ContactId] = @ContactId AND p.[IsActive] = 1
            ORDER BY p.[IsPrimary] DESC, t.[Name] ASC, p.[Id] ASC;
            """;
        cmd.Parameters.Add(new SqlParameter("@ContactId", contactId));

        var list = new List<ContactPerson>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(Map(r));
        return list;
    }

    public async Task<ContactPerson?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM {_table} p
            LEFT JOIN {_titleTable} t ON t.[Id] = p.[TitleId]
            WHERE p.[Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<int> AddAsync(ContactPerson entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
                ([ContactId],[TitleId],[FullName],[Phone],[Email],[Notes],
                 [IsPrimary],[IsActive],[Created],[CreatedById])
            VALUES
                (@ContactId,@TitleId,@FullName,@Phone,@Email,@Notes,
                 @IsPrimary,@IsActive,SYSUTCDATETIME(),@CreatedById);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        AddCommonParams(cmd, entity);
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value));

        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateAsync(ContactPerson entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET [TitleId]   = @TitleId,
                [FullName]  = @FullName,
                [Phone]     = @Phone,
                [Email]     = @Email,
                [Notes]     = @Notes,
                [IsPrimary] = @IsPrimary,
                [IsActive]  = @IsActive,
                [Updated]   = SYSUTCDATETIME(),
                [UpdatedById] = @UpdatedById
            WHERE [Id] = @Id;
            """;
        AddCommonParams(cmd, entity);
        cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)entity.UpdatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, int? deletedById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET [IsActive] = 0,
                [Updated]  = SYSUTCDATETIME(),
                [UpdatedById]= @UpdatedById
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)deletedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddCommonParams(SqlCommand cmd, ContactPerson e)
    {
        cmd.Parameters.Add(new SqlParameter("@ContactId", e.ContactId));
        cmd.Parameters.Add(new SqlParameter("@TitleId",   (object?)e.TitleId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FullName",  e.FullName ?? string.Empty));
        cmd.Parameters.Add(new SqlParameter("@Phone",     (object?)e.Phone ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Email",     (object?)e.Email ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Notes",     (object?)e.Notes ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsPrimary", e.IsPrimary));
        cmd.Parameters.Add(new SqlParameter("@IsActive",  e.IsActive));
    }

    public async Task<bool> ExistsByContactAndTitleAsync(int contactId, int titleId, int? excludeId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var sql = $"SELECT TOP(1) 1 FROM {_table} WHERE [ContactId] = @ContactId AND [TitleId] = @TitleId AND [IsActive] = 1";
        if (excludeId.HasValue && excludeId.Value > 0)
            sql += " AND [Id] <> @ExcludeId";
        cmd.CommandText = sql + ";";
        cmd.Parameters.Add(new SqlParameter("@ContactId", contactId));
        cmd.Parameters.Add(new SqlParameter("@TitleId",   titleId));
        if (excludeId.HasValue && excludeId.Value > 0)
            cmd.Parameters.Add(new SqlParameter("@ExcludeId", excludeId.Value));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value;
    }

    private static ContactPerson Map(SqlDataReader r)
    {
        // Column order from SelectColumns:
        // 0:Id, 1:ContactId, 2:FullName, 3:Phone, 4:Email, 5:Notes,
        // 6:IsPrimary, 7:IsActive, 8:Created, 9:Updated, 10:CreatedById, 11:UpdatedById,
        // 12:TitleId, 13:TitleName (from JOIN)
        return new ContactPerson
        {
            Id          = r.GetInt32(0),
            ContactId   = r.GetInt32(1),
            FullName    = r.GetString(2),
            Phone       = r.IsDBNull(3)  ? null : r.GetString(3),
            Email       = r.IsDBNull(4)  ? null : r.GetString(4),
            Notes       = r.IsDBNull(5)  ? null : r.GetString(5),
            IsPrimary   = r.GetBoolean(6),
            IsActive    = r.GetBoolean(7),
            Created     = r.GetDateTime(8),
            Updated     = r.IsDBNull(9)  ? null : r.GetDateTime(9),
            CreatedById = r.IsDBNull(10) ? null : r.GetInt32(10),
            UpdatedById = r.IsDBNull(11) ? null : r.GetInt32(11),
            TitleId     = r.IsDBNull(12) ? null : r.GetInt32(12),
            TitleName   = r.IsDBNull(13) ? null : r.GetString(13),
        };
    }
}
