using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Integration;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace CalibraHub.Application.UnitTests.Integration;

/// <summary>
/// IntegrationLookupFunctionAdminService — admin tarafindan girilen Lookup Function
/// tanimlarinin VALIDATION davranisini test eder. Validation private static metot,
/// ama CreateAsync uzerinden public API ile test edilir (mock repo: insert hic cagrilmaz).
///
/// 3 mod var: SqlFunctionName | SqlSnippet[LEGACY] | View+Key.
/// Bu testler validation kurallarinin DOGRU calistigini garanti eder. Yeni geliştirici
/// 4. mod eklemeye kalkarsa bu testlerin tamamiyla uyumlu olmaya mecbur kalir.
/// </summary>
public sealed class IntegrationLookupFunctionAdminServiceTests
{
    private static IntegrationLookupFunctionAdminService CreateSvc(out IIntegrationLookupFunctionDefinitionRepository repo)
    {
        repo = Substitute.For<IIntegrationLookupFunctionDefinitionRepository>();
        // CodeExists false don ki uniqueness gecsin
        repo.CodeExistsAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new IntegrationLookupFunctionAdminService(repo, cache);
    }

    private static SaveIntegrationLookupFunctionRequest ValidViewKeyRequest() => new(
        Id: null, Code: "ITEMS", Label: "Stok",
        Description: null, ViewName: "cbv_Guide_Items", KeyColumn: "Id",
        SqlSnippet: null, SqlFunctionName: null, SortOrder: 10, IsActive: true,
        Columns: new List<IntegrationLookupFunctionColumn> { new("Code", "Kod") });

    // ── Code / Label zorunlulugu ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_EmptyCode_Rejected()
    {
        var svc = CreateSvc(out var repo);
        var req = ValidViewKeyRequest() with { Code = "" };

        var (ok, err, id) = await svc.CreateAsync(req, "test-user", default);

        ok.Should().BeFalse();
        err.Should().Contain("Kod");
        id.Should().BeNull();
        await repo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Fact]
    public async Task CreateAsync_EmptyLabel_Rejected()
    {
        var svc = CreateSvc(out var repo);
        var req = ValidViewKeyRequest() with { Label = "  " };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);

        ok.Should().BeFalse();
        err.Should().Contain("Etiket");
        await repo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Theory]
    [InlineData("items")]            // lowercase — yasak
    [InlineData("a")]                // 1 karakter — yasak (en az 2)
    [InlineData("3STOK")]            // rakam ile basliyor — yasak
    [InlineData("CODE-WITH-DASH")]   // dash yok — yasak
    [InlineData("CODE WITH SPACE")]  // bosluk yok — yasak
    public async Task CreateAsync_InvalidCodeFormat_Rejected(string badCode)
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with { Code = badCode };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);

        ok.Should().BeFalse();
        err.Should().Contain("Kod");
    }

    [Theory]
    [InlineData("ITEMS")]
    [InlineData("PERSONNEL")]
    [InlineData("MY_FUNC_2")]
    public async Task CreateAsync_ValidCode_PassesValidation(string goodCode)
    {
        var svc = CreateSvc(out var repo);
        var req = ValidViewKeyRequest() with { Code = goodCode };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);

        ok.Should().BeTrue(err);
        await repo.Received(1).InsertAsync(Arg.Any<CalibraHub.Domain.Entities.IntegrationLookupFunctionDefinition>(), "u", Arg.Any<CancellationToken>());
    }

    // ── 3 mod — SqlFunctionName ─────────────────────────────────────────

    [Theory]
    [InlineData("dbo.fn_GetBalance")]
    [InlineData("fn_X")]
    [InlineData("schema_x.fn_y")]
    public async Task CreateAsync_ValidSqlFunctionName_Accepted(string fnName)
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with
        {
            ViewName = null, KeyColumn = null,
            SqlFunctionName = fnName,
        };

        var (ok, _, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("dbo.fn-x")]              // dash yok
    [InlineData("dbo..fn")]               // cift nokta
    [InlineData("dbo.fn x")]              // bosluk yok
    [InlineData("'); DROP TABLE")]        // SQL injection denemesi
    [InlineData("dbo.fn; SELECT 1")]      // statement separator
    public async Task CreateAsync_InvalidSqlFunctionName_Rejected(string badName)
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with
        {
            ViewName = null, KeyColumn = null,
            SqlFunctionName = badName,
        };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeFalse();
        err.Should().Contain("SQL Fonksiyon adi");
    }

    // ── 3 mod — SqlSnippet (legacy) ────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SqlSnippet_SelectOnly_Accepted()
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with
        {
            ViewName = null, KeyColumn = null,
            SqlSnippet = "SELECT TOP 1 [Code] AS [Value] FROM [Items] WHERE [Id] = @Key",
        };

        var (ok, _, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_SqlSnippet_Destructive_Rejected()
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with
        {
            ViewName = null, KeyColumn = null,
            SqlSnippet = "DROP TABLE Items",
        };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeFalse();
        err.Should().Contain("guvenlik");
    }

    // ── 3 mod — View+Key (klasik) ──────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ViewKey_BothEmpty_Rejected()
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with { ViewName = null, KeyColumn = null };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeFalse();
        err.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("v table-with-dash", "Id")]       // view adi yasak
    [InlineData("cbv_Guide", "1Id")]               // key kolon rakam ile basliyor
    [InlineData("'); DROP", "Id")]                 // injection denemesi
    public async Task CreateAsync_ViewKey_InvalidIdentifier_Rejected(string viewName, string keyColumn)
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with { ViewName = viewName, KeyColumn = keyColumn };

        var (ok, _, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeFalse();
    }

    // ── Columns ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NoColumns_Rejected()
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with { Columns = Array.Empty<IntegrationLookupFunctionColumn>() };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeFalse();
        err.Should().Contain("kolon");
    }

    [Fact]
    public async Task CreateAsync_DuplicateColumns_Rejected()
    {
        var svc = CreateSvc(out _);
        var req = ValidViewKeyRequest() with
        {
            Columns = new List<IntegrationLookupFunctionColumn>
            {
                new("Code", "Kod"),
                new("Code", "Kod"),
            }
        };

        var (ok, err, _) = await svc.CreateAsync(req, "u", default);
        ok.Should().BeFalse();
        err.Should().Contain("birden fazla");
    }

    // ── Code uniqueness ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ExistingCode_Rejected()
    {
        var svc = CreateSvc(out var repo);
        repo.CodeExistsAsync("ITEMS", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var (ok, err, _) = await svc.CreateAsync(ValidViewKeyRequest(), "u", default);

        ok.Should().BeFalse();
        err.Should().Contain("kullaniliyor");
    }
}
