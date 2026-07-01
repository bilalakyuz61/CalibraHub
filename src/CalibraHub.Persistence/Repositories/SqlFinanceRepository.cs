using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlFinanceRepository : IFinanceRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly string _tableName;

    public SqlFinanceRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor,
        IDataVisibilityFilter dvFilter)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
        _dvFilter = dvFilter;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[Contact]";
    }

    private int GetCurrentCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var raw = httpContext.User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var id))
                return id;
        }
        return 0;
    }

    public async Task<IReadOnlyCollection<Contact>> GetContactsAsync(
        byte? accountType, string? search, CancellationToken cancellationToken)
    {
        var results = new List<Contact>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();

        var where = BuildWhereClause(accountType, search);
        // Satır görünürlük kuralları (row-level security) — cari grubu vb. alan-değer kısıtları.
        var dv = await _dvFilter.BuildAsync(FormCodes.Contacts, string.Empty, "Id", cancellationToken);
        where += dv.Sql;

        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Mobile],[Email],[Website],[Address],[PostalCode],[City],[District],[Neighborhood],[CountryCode],[ContactPerson],[IsActive],[PriceGroupId],[SalesRepresentativeId],[WaPhone],[WaName],[CreatedAt],[ContactGroupId]
            FROM {_tableName}
            {where}
            ORDER BY [AccountCode];
            """;

        AddFilterParams(cmd, accountType, search);
        foreach (var p in dv.Parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapRow(reader));

        return results;
    }

    public async Task<(IReadOnlyCollection<Contact> Items, int TotalCount)> GetContactsPagedAsync(
        byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken)
    {
        var results = new List<Contact>();
        var totalCount = 0;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();

        var where = BuildWhereClause(accountType, search);
        // Satır görünürlük kuralları (row-level security).
        var dv = await _dvFilter.BuildAsync(FormCodes.Contacts, string.Empty, "Id", cancellationToken);
        where += dv.Sql;

        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Mobile],[Email],[Website],[Address],[PostalCode],[City],[District],[Neighborhood],[CountryCode],[ContactPerson],[IsActive],[PriceGroupId],[SalesRepresentativeId],[WaPhone],[WaName],[CreatedAt],[ContactGroupId],
                   COUNT(*) OVER() AS [_TotalCount]
            FROM {_tableName}
            {where}
            ORDER BY [AccountCode]
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        AddFilterParams(cmd, accountType, search);
        foreach (var p in dv.Parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);
        cmd.Parameters.Add(new SqlParameter("@Offset", offset));
        cmd.Parameters.Add(new SqlParameter("@PageSize", pageSize));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapRow(reader));
            if (totalCount == 0)
                totalCount = reader.GetInt32(reader.GetOrdinal("_TotalCount"));
        }

        // Hiç sonuç yoksa toplam sayıyı ayrı çek (OFFSET sonucu boş olabilir)
        if (results.Count == 0)
        {
            await using var countCmd = connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {_tableName} {where};";
            AddFilterParams(countCmd, accountType, search);
            foreach (var p in dv.Parameters) countCmd.Parameters.AddWithValue(p.Name, p.Value);
            totalCount = (int)(await countCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        return (results, totalCount);
    }

    private static string BuildWhereClause(byte? accountType, string? search)
    {
        var where = "WHERE [CompanyId] = @CompanyId AND [IsActive] = 1";
        if (accountType.HasValue)
            where += " AND [AccountType] = @AccountType";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND ([AccountCode] COLLATE Turkish_CI_AI LIKE @Search OR [AccountTitle] COLLATE Turkish_CI_AI LIKE @Search OR [TaxNumber] LIKE @Search)";
        return where;
    }

    private void AddFilterParams(SqlCommand cmd, byte? accountType, string? search)
    {
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        if (accountType.HasValue)
            cmd.Parameters.Add(new SqlParameter("@AccountType", accountType.Value));
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search}%"));
    }

    public async Task<Contact?> GetContactByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Mobile],[Email],[Website],[Address],[PostalCode],[City],[District],[Neighborhood],[CountryCode],[ContactPerson],[IsActive],[PriceGroupId],[SalesRepresentativeId],[WaPhone],[WaName],[CreatedAt],[ContactGroupId]
            FROM {_tableName}
            WHERE [CompanyId] = @CompanyId AND [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<Contact?> GetContactByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        // AccountCode case-insensitive collation (varsayilan SQL Server _CI_AS) — UPPER hizli match icin.
        // CompanyId scope: ayni kod farkli sirkete ait olabilir mi? AccountCode global UNIQUE
        // (CodeExistsAsync da global kontrol yapiyor) — biz de scope koymadan ariyoruz.
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[CompanyId],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Mobile],[Email],[Website],[Address],[PostalCode],[City],[District],[Neighborhood],[CountryCode],[ContactPerson],[IsActive],[PriceGroupId],[SalesRepresentativeId],[WaPhone],[WaName],[CreatedAt],[ContactGroupId]
            FROM {_tableName}
            WHERE [AccountCode] = @Code;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", code.Trim()));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<Contact?> GetContactByTaxNumberAsync(string taxNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taxNumber)) return null;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[CompanyId],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Mobile],[Email],[Website],[Address],[PostalCode],[City],[District],[Neighborhood],[CountryCode],[ContactPerson],[IsActive],[PriceGroupId],[SalesRepresentativeId],[WaPhone],[WaName],[CreatedAt],[ContactGroupId]
            FROM {_tableName}
            WHERE [TaxNumber] = @TaxNumber;
            """;
        cmd.Parameters.Add(new SqlParameter("@TaxNumber", taxNumber.Trim()));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken)
    {
        // AccountCode globalde unique — tum sirketlerde kontrol edilir (UNIQUE constraint global).
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

    public async Task<int> AddContactAsync(Contact account, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_tableName}
                ([CompanyId],[AccountType],[AccountCode],[AccountTitle],[TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Mobile],[Email],[Website],[Address],[PostalCode],[City],[District],[Neighborhood],[CountryCode],[ContactPerson],[IsActive],[PriceGroupId],[SalesRepresentativeId],[WaPhone],[WaName],[CreatedAt],[ContactGroupId])
            VALUES
                (@CompanyId,@AccountType,@AccountCode,@AccountTitle,@TaxNumber,@IdentityNumber,@TaxOffice,@Phone,@Mobile,@Email,@Website,@Address,@PostalCode,@City,@District,@Neighborhood,@CountryCode,@ContactPerson,@IsActive,@PriceGroupId,@SalesRepresentativeId,@WaPhone,@WaName,@CreatedAt,@ContactGroupId);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        // Kayit anindaki oturum kullanicisinin sirketinden cek; claim yoksa 1 (default).
        var effectiveCompanyId = account.CompanyId > 0
            ? account.CompanyId
            : (GetCurrentCompanyId() is int cid && cid > 0 ? cid : 1);
        cmd.Parameters.Add(new SqlParameter("@CompanyId", effectiveCompanyId));
        AddParams(cmd, account);
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", account.CreatedAt));
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task UpdateContactAsync(Contact account, CancellationToken cancellationToken)
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
                [Mobile]         = @Mobile,
                [Email]          = @Email,
                [Website]        = @Website,
                [Address]        = @Address,
                [PostalCode]     = @PostalCode,
                [City]           = @City,
                [District]       = @District,
                [Neighborhood]   = @Neighborhood,
                [CountryCode]    = @CountryCode,
                [ContactPerson]  = @ContactPerson,
                [IsActive]       = @IsActive,
                [PriceGroupId]   = @PriceGroupId,
                [SalesRepresentativeId] = @SalesRepresentativeId,
                [WaPhone]        = @WaPhone,
                [WaName]         = @WaName,
                [ContactGroupId] = @ContactGroupId
            WHERE [CompanyId] = @CompanyId AND [Id] = @Id;
            """;
        AddParams(cmd, account);
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id", account.Id));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteContactAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tableName} WHERE [CompanyId] = @CompanyId AND [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateContactPriceGroupAsync(int contactId, int? priceGroupId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_tableName} SET [PriceGroupId] = @PriceGroupId WHERE [CompanyId] = @CompanyId AND [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@CompanyId",    GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id",           contactId));
        cmd.Parameters.Add(new SqlParameter("@PriceGroupId", (object?)priceGroupId ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Contact>> GetContactsByPriceGroupAsync(int priceGroupId, CancellationToken cancellationToken)
    {
        var results = new List<Contact>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[AccountType],[AccountCode],[AccountTitle],
                   [TaxNumber],[IdentityNumber],[TaxOffice],[Phone],[Mobile],[Email],[Website],[Address],[PostalCode],[City],[District],[Neighborhood],[CountryCode],[ContactPerson],[IsActive],[PriceGroupId],[SalesRepresentativeId],[WaPhone],[WaName],[CreatedAt],[ContactGroupId]
            FROM {_tableName}
            WHERE [CompanyId] = @CompanyId AND [IsActive] = 1 AND [PriceGroupId] = @PriceGroupId
            ORDER BY [AccountCode];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId",    GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@PriceGroupId", priceGroupId));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapRow(reader));
        return results;
    }

    private static void AddParams(SqlCommand cmd, Contact a)
    {
        cmd.Parameters.Add(new SqlParameter("@AccountType",    (byte)a.AccountType));
        cmd.Parameters.Add(new SqlParameter("@AccountCode",    a.AccountCode));
        cmd.Parameters.Add(new SqlParameter("@AccountTitle",   a.AccountTitle));
        cmd.Parameters.Add(new SqlParameter("@TaxNumber",      (object?)a.TaxNumber      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IdentityNumber", (object?)a.IdentityNumber ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TaxOffice",      (object?)a.TaxOffice      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Phone",          (object?)a.Phone          ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Mobile",         (object?)a.Mobile         ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Email",          (object?)a.Email          ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Website",        (object?)a.Website        ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Address",        (object?)a.Address        ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PostalCode",     (object?)a.PostalCode     ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@City",           (object?)a.City           ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@District",       (object?)a.District       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Neighborhood",   (object?)a.Neighborhood   ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CountryCode",    (object?)a.CountryCode    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactPerson",  (object?)a.ContactPerson  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive",       a.IsActive));
        cmd.Parameters.Add(new SqlParameter("@PriceGroupId",   (object?)a.PriceGroupId   ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SalesRepresentativeId", (object?)a.SalesRepresentativeId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@WaPhone",        (object?)a.WaPhone        ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@WaName",         (object?)a.WaName         ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactGroupId", (object?)a.ContactGroupId ?? DBNull.Value));
    }

    private static Contact MapRow(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        CompanyId      = r.GetInt32(1),
        AccountType    = (Domain.Enums.ContactType)r.GetByte(2),
        AccountCode    = r.GetString(3),
        AccountTitle   = r.GetString(4),
        TaxNumber      = r.IsDBNull(5)  ? null : r.GetString(5),
        IdentityNumber = r.IsDBNull(6)  ? null : r.GetString(6),
        TaxOffice      = r.IsDBNull(7)  ? null : r.GetString(7),
        Phone          = r.IsDBNull(8)  ? null : r.GetString(8),
        Mobile         = r.IsDBNull(9)  ? null : r.GetString(9),
        Email          = r.IsDBNull(10) ? null : r.GetString(10),
        Website        = r.IsDBNull(11) ? null : r.GetString(11),
        Address        = r.IsDBNull(12) ? null : r.GetString(12),
        PostalCode     = r.IsDBNull(13) ? null : r.GetString(13),
        City           = r.IsDBNull(14) ? null : r.GetString(14),
        District       = r.IsDBNull(15) ? null : r.GetString(15),
        Neighborhood   = r.IsDBNull(16) ? null : r.GetString(16),
        CountryCode    = r.IsDBNull(17) ? null : r.GetString(17),
        ContactPerson  = r.IsDBNull(18) ? null : r.GetString(18),
        IsActive       = r.GetBoolean(19),
        PriceGroupId   = r.IsDBNull(20) ? null : r.GetInt32(20),
        SalesRepresentativeId = r.IsDBNull(21) ? null : r.GetInt32(21),
        WaPhone        = r.IsDBNull(22) ? null : r.GetString(22),
        WaName         = r.IsDBNull(23) ? null : r.GetString(23),
        CreatedAt      = r.GetDateTime(24),
        ContactGroupId = r.IsDBNull(25) ? null : r.GetInt32(25)
    };
}
