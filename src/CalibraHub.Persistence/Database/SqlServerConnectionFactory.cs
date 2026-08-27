using System.Data.Common;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Database;

public sealed class SqlServerConnectionFactory : IDbConnectionFactory
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
        await ApplyCompanyContextAsync(connection, cancellationToken);
        return connection;
    }

    /// <summary>
    /// Satır Düzeyi Güvenlik'in (RLS) okuduğu oturum bağlamını yazar — kiracı ayrımının
    /// TEK enjeksiyon noktası. Politika yüklemi bu değeri okur ve her sorguya
    /// <c>CompanyId = &lt;bu değer&gt;</c> şartını kendisi ekler; sorguların hiçbirinde
    /// elle süzgeç yazılmaz.
    ///
    /// <para><b>Bağlam yalnız kimlikli istekte yazılır.</b> Şirketi çözülemeyen yollar
    /// (açılış/migration, arka plan işleri, kurulum sihirbazı) bağlamsız kalır ve yüklem
    /// onları SÜZMEZ — bilinçli "fail-open" seçimi. Fail-closed yapmak teoride daha güvenli
    /// olurdu ama uygulamanın kendi kurulumunu ve zamanlanmış işlerini sessizce boş veri
    /// görmeye mahkûm ederdi; bu projede sessiz boş dönüş en pahalı hata sınıfı.
    /// Bu yol bugünkü davranışa göre bir gerileme DEĞİL: bugün zaten hiçbir sorgu süzülmüyor.
    /// Kimlikli kullanıcı trafiği — yani sızıntının gerçekten olabileceği yer — korunur.</para>
    ///
    /// <para><c>@read_only = 1</c>: değer yazıldıktan sonra aynı bağlantıda DEĞİŞTİRİLEMEZ.
    /// Bir sorgu araya girip bağlamı başka şirkete çevirerek yüklemi atlatamaz. Havuzdan
    /// yeniden kullanılan bağlantıda sp_reset_connection bağlamı temizler, çakışma olmaz.</para>
    /// </summary>
    private async Task ApplyCompanyContextAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var companyId = ResolveCurrentCompanyId();
        if (companyId <= 0) return;

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context @key = N'CompanyId', @value = @Cid, @read_only = 1;";
            cmd.Parameters.Add(new SqlParameter("@Cid", companyId));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Bağlam yazılamazsa bağlantıyı ÇÖKERTME — istek çalışsın, ama sessiz kalma:
            // bu durumda o bağlantı süzülmez ve bunun görünmesi gerekir.
            Console.WriteLine($"[RLS] Sirket baglami yazilamadi (companyId={companyId}): {ex.Message}");
        }
    }

    async Task<DbConnection> IDbConnectionFactory.OpenConnectionAsync(CancellationToken ct)
        => await OpenConnectionAsync(ct);

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
    /// Kiracı süzgeçlerinde (<c>WHERE CompanyId = @CompanyId</c>) kullanılacak değer.
    ///
    /// <para>Kimlikli istekte oturumun şirketi; kimliksiz yollarda (açılış, migration, arka plan
    /// işleri, kurulum sihirbazı) <b>veritabanının sahibi şirket</b>. Sıfır dönmek fail-closed
    /// olurdu ve arka plan işleri sessizce boş veri görürdü — bu projede en pahalı hata sınıfı
    /// (2026-08-27 kullanıcı kararı: "CompanyId = 1").</para>
    ///
    /// <para>Sahip şirket sabit <c>1</c> DEĞİL, sorgulanır: her veritabanında sahip farklı bir
    /// id taşıyabilir; sabitlemek başka bir şirketin verisine yazmak demekti. Bugünkü kurulumda
    /// sonuç zaten 1'dir. Bağlantı dizesi başına bir kez çözülür ve önbelleğe alınır.</para>
    /// </summary>
    public int ResolveEffectiveCompanyId()
    {
        var current = ResolveCurrentCompanyId();
        if (current > 0) return current;

        var key = ResolveConnectionString();
        if (OwnerCompanyCache.TryGetValue(key, out var cached)) return cached;

        var owner = 0;
        try
        {
            using var conn = new SqlConnection(EnsureMars(key));
            conn.Open();
            using var cmd = conn.CreateCommand();
            // TEST_ sirketleri ELENIR: bir test sirketi canli sirketle ayni DB'ye dustugunde
            // (DatabaseName bos birakilinca oluyor) gecmis veriyi ona devretmek olurdu.
            cmd.CommandText = """
                IF OBJECT_ID('dbo.Company', 'U') IS NULL SELECT CAST(NULL AS INT);
                ELSE SELECT TOP (1) [Id] FROM dbo.[Company]
                     WHERE [name] IS NULL OR [name] NOT LIKE 'TEST!_%' ESCAPE '!'
                     ORDER BY [Id];
                """;
            var v = cmd.ExecuteScalar();
            if (v is not null && v != DBNull.Value) owner = Convert.ToInt32(v);
        }
        catch (Exception ex)
        {
            // Cozulemezse 0 doner ve suzgec hicbir sey getirmez. Sessiz kalma: sebebi yaz.
            Console.WriteLine($"[Tenant] Sahip sirket cozulemedi, suzgec bos donecek: {ex.Message}");
        }

        OwnerCompanyCache[key] = owner;
        return owner;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> OwnerCompanyCache = new();

    /// <summary>
    /// Mevcut request'in company_id claim degerini dondurur. Authenticated degilse 0 doner.
    /// Kiraci suzgeci icin <see cref="ResolveEffectiveCompanyId"/> kullanin — bu metod
    /// ham claim degerini verir, geri donus degeri YOKTUR.
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

        // Kimliksiz endpoint'ler (QuickApproval vb.) için controller Items'a override koyar.
        if (httpContext?.Items.TryGetValue("__override_company_id", out var overrideVal) == true
            && overrideVal is int overrideId
            && _registry.TryGet(overrideId, out var overrideConn))
        {
            return overrideConn;
        }

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
