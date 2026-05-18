using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CalibraHub.Application.UnitTests.Integration;

/// <summary>
/// IntegrationOnSaveDispatcher — Save sonrasi otomatik trigger'lari fire eden servis.
/// Bu testler INPUT VALIDATION davranisini garanti eder; gercek background calismayi
/// (Task.Run icindeki path) test etmeye gerek yok cunku zaten DB + HTTP gerekir
/// (integration test kapsami). Burada amac: kotu input no-op olmali, iyi input
/// scope acmali. Dispatcher fire-and-forget oldugundan exception sizdirmamali.
/// </summary>
public sealed class IntegrationOnSaveDispatcherTests
{
    private static IIntegrationOnSaveDispatcher CreateDispatcher(out IServiceScopeFactory scopeFactory)
    {
        // Scope factory mock — Task.Run icindeki scope acma cagrilari yakalanir.
        // Repo+runner mock'lari da burada konfigure edilir ki gercek DB calismasin.
        scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope    = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        var repo     = Substitute.For<IIntegrationRepository>();
        var runner   = Substitute.For<IIntegrationRunner>();

        scope.ServiceProvider.Returns(provider);
        scopeFactory.CreateScope().Returns(scope);
        provider.GetService(typeof(IIntegrationRepository)).Returns(repo);
        provider.GetService(typeof(IIntegrationRunner)).Returns(runner);

        // Repo + runner null degil ama bos cevap — bu testlerde gercek calismaya ihtiyac yok,
        // amac sadece "scope acildi mi?" dogrulamak.
        repo.ListByFormCodeAsync(Arg.Any<string>(), Arg.Any<CalibraHub.Domain.Enums.IntegrationTriggerType>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CalibraHub.Domain.Entities.Integration>>(Array.Empty<CalibraHub.Domain.Entities.Integration>()));

        return new IntegrationOnSaveDispatcher(scopeFactory, NullLogger<IntegrationOnSaveDispatcher>.Instance);
    }

    // ── Input validation: kotu input no-op ─────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FireOnSave_EmptyRecordId_DoesNothing(string? recordId)
    {
        var dispatcher = CreateDispatcher(out var scopes);
        dispatcher.FireOnSave("SALES_ORDER_EDIT", recordId!);
        // Background task hic baslamamali (scope.CreateScope cagrilmamali)
        scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public void FireOnSave_EmptyFormCodes_DoesNothing()
    {
        var dispatcher = CreateDispatcher(out var scopes);
        dispatcher.FireOnSave(Array.Empty<string>(), "42");
        scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public void FireOnSave_NullFormCodes_DoesNothing()
    {
        var dispatcher = CreateDispatcher(out var scopes);
        dispatcher.FireOnSave((IEnumerable<string>)null!, "42");
        scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public void FireOnSave_AllWhitespaceFormCodes_DoesNothing()
    {
        var dispatcher = CreateDispatcher(out var scopes);
        dispatcher.FireOnSave(new[] { "", "  ", null! }, "42");
        scopes.DidNotReceive().CreateScope();
    }

    // ── Happy path: scope aciliyor + bekleme yok ──────────────────────

    [Fact]
    public async Task FireOnSave_ValidInput_BackgroundScopeCreated()
    {
        var dispatcher = CreateDispatcher(out var scopes);
        dispatcher.FireOnSave("SALES_ORDER_EDIT", "42");

        // Fire-and-forget — sync metot derhal doner; Task.Run icindeki scope
        // acilisi icin minimal bekleme. Bu testin amaci: scope ACILDIGINI dogrulamak.
        // Production senaryosunda da bu uretim Task.Run scope'unun olmasi gerek.
        await Task.Delay(100);

        scopes.Received(1).CreateScope();
    }

    [Fact]
    public async Task FireOnSave_MultipleDuplicateFormCodes_DedupedAndScopeCreatedOnce()
    {
        var dispatcher = CreateDispatcher(out var scopes);
        dispatcher.FireOnSave(
            new[] { "SALES_ORDER_EDIT", "SALES_ORDER_EDIT", "sales_order_edit" }, // case-insensitive dedup
            "42");
        await Task.Delay(100);

        // Tek bir scope acilmali — 3 input olsa da dedup ile background tek calistirilir
        scopes.Received(1).CreateScope();
    }

    // ── Fire-and-forget: caller asla beklemez ─────────────────────────

    [Fact]
    public void FireOnSave_VoidReturn_NotAsync()
    {
        // Sözel kontrat: dispatcher.FireOnSave VOID donmeli (async Task degil).
        // Kullanici Save bekleme suresini geciktirmemek icin bu kritik.
        var method = typeof(IIntegrationOnSaveDispatcher).GetMethod(
            nameof(IIntegrationOnSaveDispatcher.FireOnSave),
            new[] { typeof(string), typeof(string), typeof(string) });
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void),
            "Fire-and-forget kontrat: caller'i bekletmemek icin sync void donmeli.");
    }
}
