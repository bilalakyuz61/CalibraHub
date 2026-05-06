using System.Security.Claims;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Database;

public sealed class SqlServerConnectionFactory
{
    private readonly string _systemConnectionString;
    private readonly CompanyConnectionRegistry _registry;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SqlServerConnectionFactory(
        CalibraDatabaseOptions options,
        CompanyConnectionRegistry registry,
        IHttpContextAccessor httpContextAccessor)
    {
        _systemConnectionString = EnsureMars(options.ConnectionString);
        _registry = registry;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>Always opens the system (global) database connection.</summary>
    public async Task<SqlConnection> OpenSystemConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_systemConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Opens a connection for the current request's company.
    /// Falls back to the system connection string if the company has no dedicated DB.
    /// </summary>
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = EnsureMars(ResolveConnectionString());
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string EnsureMars(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            MultipleActiveResultSets = true
        };
        return builder.ConnectionString;
    }

    public async Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseExistsForConnectionStringAsync(_systemConnectionString, cancellationToken);
    }

    public async Task EnsureCompanyDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await EnsureDatabaseExistsForConnectionStringAsync(connectionString, cancellationToken);
    }

    /// <summary>
    /// Belirli bir sirketin connection string'ini dondurur (per-company veya system fallback).
    /// Fire-and-forget event'leri icin HttpContext disinda kullanilabilir.
    /// </summary>
    public string ResolveConnectionStringForCompany(int companyId)
    {
        return _registry.TryGet(companyId, out var connStr) ? connStr : _systemConnectionString;
    }

    /// <summary>
    /// Mevcut request'in company_id claim degerini dondurur. Authenticated degilse 0 doner —
    /// SQL filtrelerinde "WHERE CompanyId = @CompanyId" calisir, 0 ile eslesen kayit yoksa bos liste doner.
    /// </summary>
    public int ResolveCurrentCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true) return 0;
        var raw = httpContext.User.FindFirst("company_id")?.Value;
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private string ResolveConnectionString()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var companyIdClaim = httpContext.User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrWhiteSpace(companyIdClaim) &&
                int.TryParse(companyIdClaim, out var companyId) &&
                _registry.TryGet(companyId, out var perCompanyConnection))
            {
                return perCompanyConnection;
            }
        }
        return _systemConnectionString;
    }

    private static async Task EnsureDatabaseExistsForConnectionStringAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog?.Trim();

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("ConnectionString icinde veritabani adi zorunludur.");
        }

        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };

        await using var connection = new SqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF DB_ID(@DatabaseName) IS NULL
            BEGIN
                DECLARE @sql NVARCHAR(512) = N'CREATE DATABASE [' + REPLACE(@DatabaseName, N']', N']]') + N']';
                EXEC(@sql);
            END;
            """;
        command.Parameters.Add(new SqlParameter("@DatabaseName", databaseName));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
