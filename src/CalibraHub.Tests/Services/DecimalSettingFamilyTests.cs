using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CalibraHub.Tests.Services;

/// <summary>
/// Ondalık belge-ailesi konsolidasyonu (2026-07-07) — üst/kalem/yeni/düzenleme form
/// kodları TEK kök koda iner; kullanıcı belge başına tek tanım yapar. Bu testler o
/// sözleşmeyi kilitler: grid'in _LINES kodu ile backend'in kök kodu aynı ayarı okur,
/// admin listesi belge başına tek satır üretir (etiket SubModule'den).
/// </summary>
public sealed class DecimalSettingFamilyTests
{
    // ── Saf normalizasyon kuralı ──────────────────────────────────────────

    [Theory]
    [InlineData("SALES_QUOTE_LINES",     "SALES_QUOTE")]
    [InlineData("SALES_QUOTE_EDIT",      "SALES_QUOTE")]
    [InlineData("SALES_QUOTE_NEW",       "SALES_QUOTE")]
    [InlineData("SALES_QUOTE",           "SALES_QUOTE")]
    [InlineData("STOCK_IN_LINES",        "STOCK_IN")]
    [InlineData("STOCK_OUT_LINES",       "STOCK_OUT")]
    [InlineData("TRANSFER_LINES",        "TRANSFER")]
    [InlineData("PURCHASE_REQUEST_LINES","PURCHASE_REQUEST")]
    [InlineData("INVENTORY_COUNT_LINES", "INVENTORY_COUNT")]
    [InlineData("PURCHASE_FULFILLMENT",  "PURCHASE_FULFILLMENT")] // suffix değil — dokunulmaz
    [InlineData("*",                     "*")]
    public void NormalizeFormCode_AileKokuneIner(string input, string expected)
        => Assert.Equal(expected, DecimalSettingService.NormalizeFormCode(input));

    // ── Uçtan uca: alias kod kök ayarı okur ───────────────────────────────

    [Fact]
    public async Task GetEffective_LinesKodu_KokAyariniOkur()
    {
        // Kök SALES_QUOTE'a miktar=3 kaydedilmiş; grid SALES_QUOTE_LINES ile sorar.
        var svc = CreateService(settings:
        [
            new DecimalSetting { CompanyId = 1, FormCode = "SALES_QUOTE", QuantityDecimals = 3 },
        ]);

        var dec = await svc.GetEffectiveAsync("SALES_QUOTE_LINES", default);

        Assert.Equal("SALES_QUOTE", dec.FormCode);
        Assert.Equal(3, dec.Quantity);
        Assert.Equal("form", dec.Source);
    }

    [Fact]
    public async Task Save_AliasKodlaGelse_KokKodaYazar()
    {
        var repo = new FakeDecimalRepo([]);
        var svc = CreateService(repo);

        await svc.SaveAsync(new SaveDecimalSettingRequest("STOCK_IN_LINES", 1, 2, 4, 2, 2, 4), userId: null, default);

        var saved = Assert.Single(repo.Upserted);
        Assert.Equal("STOCK_IN", saved.FormCode);
    }

    // ── Admin listesi: belge başına tek satır, etiket SubModule ──────────

    [Fact]
    public async Task GetPageRows_AileyeTekSatir_EtiketSubModule()
    {
        var forms = new[]
        {
            Form(1, "STOCK_IN",       "Üst Bilgi",     "Lojistik", "Ambar Giriş", 335),
            Form(2, "STOCK_IN_LINES", "Kalem Bilgisi", "Lojistik", "Ambar Giriş", 336),
            Form(3, "NOTES",          "Notlar",        "Genel",    null,          10),
        };
        var svc = CreateService(forms: forms);

        var rows = await svc.GetPageRowsAsync(default);

        // '*' + STOCK_IN ailesi (tek satır) + NOTES = 3 satır
        Assert.Equal(3, rows.Count);
        var stockRow = Assert.Single(rows, r => r.FormCode == "STOCK_IN");
        Assert.Equal("Ambar Giriş", stockRow.FormName);
        Assert.DoesNotContain(rows, r => r.FormCode == "STOCK_IN_LINES");
        Assert.Equal("Notlar", Assert.Single(rows, r => r.FormCode == "NOTES").FormName);
    }

    // ── Fixture ───────────────────────────────────────────────────────────

    private static FormDto Form(int id, string code, string name, string? module, string? subModule, int sort)
        => new(id, code, name, module, subModule, sort, IsActive: true, BaseTable: null, BaseRecordKey: null);

    private static DecimalSettingService CreateService(
        FakeDecimalRepo? repo = null,
        IReadOnlyList<DecimalSetting>? settings = null,
        IReadOnlyList<FormDto>? forms = null) => new(
        repo ?? new FakeDecimalRepo(settings ?? []),
        new FakeFormRepo(forms ?? []),
        new FakeCompanyProvider(1),
        new MemoryCache(new MemoryCacheOptions()));

    private sealed class FakeCompanyProvider(int companyId) : ICurrentCompanyProvider
    {
        public int GetCurrentCompanyId() => companyId;
        public string? GetBaseUrl() => null;
    }

    private sealed class FakeDecimalRepo(IReadOnlyList<DecimalSetting> settings) : IDecimalSettingRepository
    {
        public List<DecimalSetting> Upserted { get; } = [];

        public Task<IReadOnlyList<DecimalSetting>> GetAllAsync(int companyId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DecimalSetting>>(
                settings.Where(s => s.CompanyId == companyId).ToList());

        public Task<DecimalSetting?> GetAsync(int companyId, string formCode, CancellationToken ct)
            => Task.FromResult(settings.FirstOrDefault(s =>
                s.CompanyId == companyId &&
                string.Equals(s.FormCode, formCode, StringComparison.OrdinalIgnoreCase)));

        public Task UpsertAsync(DecimalSetting setting, CancellationToken ct)
        {
            Upserted.Add(setting);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int companyId, string formCode, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeFormRepo(IReadOnlyList<FormDto> forms) : IFormRepository
    {
        public Task<IReadOnlyCollection<FormDto>> GetAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<FormDto>>(forms.ToList());

        public Task<FormDto?> GetByIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<FormDto?> GetByCodeAsync(string formCode, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CreateAsync(CreateFormRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(UpdateFormRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }
}
