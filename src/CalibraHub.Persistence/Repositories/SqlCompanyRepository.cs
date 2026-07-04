using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlCompanyRepository : ICompanyRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlCompanyRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[Company]";
    }

    public async Task<IReadOnlyCollection<Company>> GetAllAsync(CancellationToken cancellationToken)
    {
        var companies = new List<Company>();

        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [Name], [Title], [Address], [City], [District], [PostalCode],
                   [TaxOffice], [TaxNumber],
                   [IsEDocumentApprovalEnabled], [IsActive], [ConnectionString],
                   [PublicUrl]
            FROM {_tableName}
            ORDER BY [Name];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            companies.Add(MapCompany(reader));
        }

        return companies;
    }

    public async Task<Company?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [Name], [Title], [Address], [City], [District], [PostalCode],
                   [TaxOffice], [TaxNumber],
                   [IsEDocumentApprovalEnabled], [IsActive], [ConnectionString],
                   [PublicUrl]
            FROM {_tableName}
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return MapCompany(reader);
    }

    public async Task<int> AddAsync(Company company, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // [id] INT IDENTITY — INSERT'e dahil edilmez, OUTPUT ile okunur
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([Name], [Title], [Address], [City], [District], [PostalCode],
                 [TaxOffice], [TaxNumber],
                 [IsEDocumentApprovalEnabled], [IsActive], [ConnectionString], [PublicUrl],
                 [Created], [Updated])
            OUTPUT INSERTED.[Id]
            VALUES
                (@Name, @Title, @Address, @City, @District, @PostalCode,
                 @TaxOffice, @TaxNumber,
                 @IsEDocumentApprovalEnabled, @IsActive, @ConnectionString, @PublicBaseUrl,
                 @CreatedAt, @UpdatedAt);
            """;
        AddInsertParameters(command, company);
        command.Parameters.Add(new SqlParameter("@CreatedAt", DateTime.Now));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Company company, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET [Name] = @Name,
                [Title] = @Title,
                [Address] = @Address,
                [City] = @City,
                [District] = @District,
                [PostalCode] = @PostalCode,
                [TaxOffice] = @TaxOffice,
                [TaxNumber] = @TaxNumber,
                [IsEDocumentApprovalEnabled] = @IsEDocumentApprovalEnabled,
                [IsActive] = @IsActive,
                [ConnectionString] = @ConnectionString,
                [PublicUrl] = @PublicBaseUrl,
                [Updated] = @UpdatedAt
            WHERE [Id] = @Id;
            """;
        AddInsertParameters(command, company);
        command.Parameters.Add(new SqlParameter("@Id", company.Id));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddInsertParameters(SqlCommand command, Company company)
    {
        command.Parameters.Add(new SqlParameter("@Name", company.Name));
        command.Parameters.Add(new SqlParameter("@Title", company.Title));
        command.Parameters.Add(new SqlParameter("@Address", company.Address));
        command.Parameters.Add(new SqlParameter("@City", (object?)company.City ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@District", (object?)company.District ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@PostalCode", (object?)company.PostalCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@TaxOffice", company.TaxOffice));
        command.Parameters.Add(new SqlParameter("@TaxNumber", company.TaxNumber));
        command.Parameters.Add(new SqlParameter("@IsEDocumentApprovalEnabled", company.IsEDocumentApprovalEnabled));
        command.Parameters.Add(new SqlParameter("@IsActive", company.IsActive));
        command.Parameters.Add(new SqlParameter("@ConnectionString", (object?)company.DatabaseConnectionString ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@PublicBaseUrl", (object?)company.PublicBaseUrl ?? DBNull.Value));
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_tableName} WHERE [Id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Company MapCompany(SqlDataReader r)
    {
        var company = new Company
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Name = r.GetString(r.GetOrdinal("Name")),
            Title = r.GetString(r.GetOrdinal("Title")),
            Address = r.GetString(r.GetOrdinal("Address")),
            City = r.IsDBNull(r.GetOrdinal("City")) ? null : r.GetString(r.GetOrdinal("City")),
            District = r.IsDBNull(r.GetOrdinal("District")) ? null : r.GetString(r.GetOrdinal("District")),
            PostalCode = r.IsDBNull(r.GetOrdinal("PostalCode")) ? null : r.GetString(r.GetOrdinal("PostalCode")),
            TaxOffice = r.GetString(r.GetOrdinal("TaxOffice")),
            TaxNumber = r.GetString(r.GetOrdinal("TaxNumber")),
            IsEDocumentApprovalEnabled = r.GetBoolean(r.GetOrdinal("IsEDocumentApprovalEnabled")),
            DatabaseConnectionString = r.IsDBNull(r.GetOrdinal("ConnectionString")) ? null : r.GetString(r.GetOrdinal("ConnectionString")),
            PublicBaseUrl = r.IsDBNull(r.GetOrdinal("PublicUrl")) ? null : r.GetString(r.GetOrdinal("PublicUrl"))
        };

        if (!r.GetBoolean(r.GetOrdinal("IsActive")))
            company.Deactivate();

        return company;
    }
}
