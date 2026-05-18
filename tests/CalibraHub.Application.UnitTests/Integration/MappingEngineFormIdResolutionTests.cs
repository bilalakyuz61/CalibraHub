using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Integration;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using NSubstitute;

namespace CalibraHub.Application.UnitTests.Integration;

/// <summary>
/// MappingEngine — SourceFormCode → Forms.Id (INT) lookup + SQL function @P1
/// binding davranisi. Rapor §2.3 SqlFunction param refactor (formCode→formId).
///
/// Engine SourceFormCode'u IFormRepository.GetByCodeAsync ile cevirip
/// IIntegrationLookupFunctionRegistry.ExecuteDbFunctionAsync'e int formId verir.
/// </summary>
public sealed class MappingEngineFormIdResolutionTests
{
    private static CalibraHub.Domain.Entities.Integration BuildIntegration(string formCode, string targetPath, string sourceValue) =>
        new()
        {
            Name = "TestIntegration",
            SourceFormCode = formCode,
            Mappings = new List<IntegrationMapping>
            {
                new()
                {
                    TargetPath    = targetPath,
                    SourceType    = IntegrationSourceType.Function,
                    SourceValue   = sourceValue, // "dbo.fn_X" → ExecuteDbFunctionAsync yolu
                    SourceSection = "Header",
                    SortOrder     = 0,
                    LookupSourceField = "DocumentId",
                    LookupParam       = "MANUEL_DEGER",
                }
            }
        };

    [Fact]
    public async Task BuildAsync_ResolvesFormIdFromFormCode_AndPassesIntToFunctionRegistry()
    {
        // FormRepository: "SALES_ORDER_NEW" → Forms.Id=42
        var formRepo = Substitute.For<IFormRepository>();
        formRepo.GetByCodeAsync("SALES_ORDER_NEW", Arg.Any<CancellationToken>())
            .Returns(new FormDto(
                Id: 42, FormCode: "SALES_ORDER_NEW", FormName: "Satis Siparisi",
                Module: "Sales", SubModule: null, SortOrder: 0, IsActive: true,
                BaseTable: "document", BaseRecordKey: "Id"));

        var functions = Substitute.For<IIntegrationLookupFunctionRegistry>();
        functions.ExecuteDbFunctionAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>("RESULT"));

        var guide = Substitute.For<IGuideService>();
        var engine = new MappingEngine(guide, functions, formRepo);

        var integration = BuildIntegration("SALES_ORDER_NEW", "Kalems[].DEPO_KODU", "dbo.cbsf_DenemeFonksiyon");
        var headerData = new Dictionary<string, object?> { ["DocumentId"] = 12345 };

        // Act
        await engine.BuildAsync(integration, headerData, linesData: null, combinationCodes: null, CancellationToken.None);

        // Assert: ExecuteDbFunctionAsync formId=42 (INT) ile cagrildi
        await functions.Received(1).ExecuteDbFunctionAsync(
            "dbo.cbsf_DenemeFonksiyon",
            42,                  // ← formId INT (Forms.Id lookup sonucu)
            "12345",             // @P2 keyValue (DocumentId form alanindan)
            "MANUEL_DEGER",      // @P3 manualParam
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WhenFormCodeNotFound_PassesZeroFormId()
    {
        // FormRepository: bulunamadi
        var formRepo = Substitute.For<IFormRepository>();
        formRepo.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((FormDto?)null);

        var functions = Substitute.For<IIntegrationLookupFunctionRegistry>();
        functions.ExecuteDbFunctionAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>(null));

        var guide = Substitute.For<IGuideService>();
        var engine = new MappingEngine(guide, functions, formRepo);

        var integration = BuildIntegration("UNKNOWN_FORM", "Result", "dbo.cbsf_DenemeFonksiyon");
        var headerData = new Dictionary<string, object?>();

        await engine.BuildAsync(integration, headerData, linesData: null, combinationCodes: null, CancellationToken.None);

        // formId=0 (cozumlenememe) → registry NULL'a cevirir
        await functions.Received(1).ExecuteDbFunctionAsync(
            Arg.Any<string>(),
            0,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WhenFormRepositoryNotInjected_PassesZeroFormId()
    {
        // Backward-compat: formRepo null ise (test/legacy DI), formId=0 ile devam et
        var functions = Substitute.For<IIntegrationLookupFunctionRegistry>();
        functions.ExecuteDbFunctionAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>(null));

        var guide = Substitute.For<IGuideService>();
        var engine = new MappingEngine(guide, functions, formRepo: null);

        var integration = BuildIntegration("SALES_ORDER_NEW", "Result", "dbo.cbsf_DenemeFonksiyon");

        await engine.BuildAsync(integration, new Dictionary<string, object?>(), linesData: null,
            combinationCodes: null, CancellationToken.None);

        await functions.Received(1).ExecuteDbFunctionAsync(
            Arg.Any<string>(),
            0,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_LegacyFunctionWrapperPath_AlsoReceivesFormId()
    {
        // SourceValue nokta icermiyor → wrapper path (ResolveWithParamsAsync)
        var formRepo = Substitute.For<IFormRepository>();
        formRepo.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FormDto(7, "X", "X", null, null, 0, true, "t", "Id"));

        var functions = Substitute.For<IIntegrationLookupFunctionRegistry>();
        functions.ResolveWithParamsAsync(
                Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>("WRAPPER_RESULT"));

        var guide = Substitute.For<IGuideService>();
        var engine = new MappingEngine(guide, functions, formRepo);

        var integration = BuildIntegration("X", "Result", "CARI_BAKIYE");  // legacy code, nokta yok

        await engine.BuildAsync(integration, new Dictionary<string, object?>(), linesData: null,
            combinationCodes: null, CancellationToken.None);

        await functions.Received(1).ResolveWithParamsAsync(
            "CARI_BAKIYE",
            7,                    // ← formId INT
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
