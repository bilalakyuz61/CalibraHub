using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlFinanceRepository : IFinanceRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlFinanceRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[ContactAccounts]";
    }

    public async Task<IReadOnlyCollection<ContactAccount>> GetContactAccountsAsync(
        byte? accountType, string? search, CancellationToken cancellationToken)
    {
        var results = new List<ContactAccount>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();

        var where = BuildWhereClause(accountType, search);

        cmd.CommandText = $"""
            SELECT [Id],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Email],[Address],[City],[IsActive],[PriceGroupId],[CreatedAt]
            FROM {_tableName}
            {where}
            ORDER BY [AccountCode];
            """;

        AddFilterParams(cmd, accountType, search);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapRow(reader));

        return results;
    }

    public async Task<(IReadOnlyCollection<ContactAccount> Items, int TotalCount)> GetContactAccountsPagedAsync(
        byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken)
    {
        var results = new List<ContactAccount>();
        var totalCount = 0;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();

        var where = BuildWhereClause(accountType, search);

        cmd.CommandText = $"""
            SELECT [Id],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Email],[Address],[City],[IsActive],[PriceGroupId],[CreatedAt],
                   COUNT(*) OVER() AS [_TotalCount]
            FROM {_tableName}
            {where}
            ORDER BY [AccountCode]
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        AddFilterParams(cmd, accountType, search);
        cmd.Parameters.Add(new SqlParameter("@Offset", offset));
        cmd.Parameters.Add(new SqlParameter("@PageSize", pageSize));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapRow(reader));
            if (totalCount == 0)
                totalCount = reader.GetInt32(14); // _TotalCount
        }

        // Hiç sonuç yoksa toplam sayıyı ayrı çek (OFFSET sonucu boş olabilir)
        if (results.Count == 0)
        {
            await using var countCmd = connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {_tableName} {where};";
            AddFilterParams(countCmd, accountType, search);
            totalCount = (int)(await countCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        return (results, totalCount);
    }

    private static string BuildWhereClause(byte? accountType, string? search)
    {
        var where = "WHERE [IsActive] = 1";
        if (accountType.HasValue)
            where += " AND [AccountType] = @AccountType";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND ([AccountCode] LIKE @Search OR [AccountTitle] LIKE @Search OR [TaxNumber] LIKE @Search)";
        return where;
    }

    private static void AddFilterParams(SqlCommand cmd, byte? accountType, string? search)
    {
        if (accountType.HasValue)
            cmd.Parameters.Add(new SqlParameter("@AccountType", accountType.Value));
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search}%"));
    }

    public async Task<ContactAccount?> GetContactAccountByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Email],[Address],[City],[IsActive],[PriceGroupId],[CreatedAt]
            FROM {_tableName}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = excludeId.HasValue
            ? $"SELECT COUNT(1) FROM {_tableName} WHERE [AccountCode] = @Code AND [Id] <> @ExcludeId;"
            : $"SELECT COUNT(1) FROM {_tableName} WHERE [AccountCode] = @Code;";
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        if (excludeId.HasValue)
            cmd.Parameters.Add(new SqlParameter("@ExcludeId", excludeId.Value));
        var count = (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        return count > 0;
    }

    public async Task<int> AddContactAccountAsync(ContactAccount account, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_tableName}
                ([AccountType],[AccountCode],[AccountTitle],[TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Email],[Address],[City],[IsActive],[PriceGroupId],[CreatedAt])
            VALUES
                (@AccountType,@AccountCode,@AccountTitle,@TaxNumber,@IdentityNumber,@TaxOffice,@Phone,@Email,@Address,@City,@IsActive,@PriceGroupId,@CreatedAt);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        AddParams(cmd, account);
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", account.CreatedAt));
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task UpdateContactAccountAsync(ContactAccount account, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_tableName}
            SET [AccountType]    = @AccountType,
                [AccountCode]    = @AccountCode,
                [AccountTitle]   = @AccountTitle,
                [TaxNumber]      = @TaxNumber,
                [IdentityNumber] = @IdentityNumber,
                [TaxOffice]      = @TaxOffice,
                [Phone]          = @Phone,
                [Email]          = @Email,
                [Address]        = @Address,
                [City]           = @City,
                [IsActive]       = @IsActive,
                [PriceGroupId]   = @PriceGroupId
            WHERE [Id] = @Id;
            """;
        AddParams(cmd, account);
        cmd.Parameters.Add(new SqlParameter("@Id", account.Id));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteContactAccountAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tableName} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParams(SqlCommand cmd, ContactAccount a)
    {
        cmd.Parameters.Add(new SqlParameter("@AccountType",    a.AccountType));
        cmd.Parameters.Add(new SqlParameter("@AccountCode",    a.AccountCode));
        cmd.Parameters.Add(new SqlParameter("@AccountTitle",   a.AccountTitle));
        cmd.Parameters.Add(new SqlParameter("@TaxNumber",      (object?)a.TaxNumber      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IdentityNumber", (object?)a.IdentityNumber ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TaxOffice",      (object?)a.TaxOffice      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Phone",          (object?)a.Phone          ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Email",          (object?)a.Email          ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Address",        (object?)a.Address        ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@City",           (object?)a.City           ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive",       a.IsActive));
        cmd.Parameters.Add(new SqlParameter("@PriceGroupId",   (object?)a.PriceGroupId   ?? DBNull.Value));
    }

    private static ContactAccount MapRow(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        AccountType    = r.GetByte(1),
        AccountCode    = r.GetString(2),
        AccountTitle   = r.GetString(3),
        TaxNumber      = r.IsDBNull(4)  ? null : r.GetString(4),
        IdentityNumber = r.IsDBNull(5)  ? null : r.GetString(5),
        TaxOffice      = r.IsDBNull(6)  ? null : r.GetString(6),
        Phone          = r.IsDBNull(7)  ? null : r.GetString(7),
        Email          = r.IsDBNull(8)  ? null : r.GetString(8),
        Address        = r.IsDBNull(9)  ? null : r.GetString(9),
        City           = r.IsDBNull(10) ? null : r.GetString(10),
        IsActive       = r.GetBoolean(11),
        PriceGroupId   = r.IsDBNull(12) ? null : r.GetInt32(12),
        CreatedAt      = r.GetDateTime(13)
    };
}
