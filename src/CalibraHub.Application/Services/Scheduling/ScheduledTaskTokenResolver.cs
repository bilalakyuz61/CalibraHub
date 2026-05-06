using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Scheduled task SQL prosedur parametrelerinde {COMPANY_ID}, {INTEGRATION_DB} gibi
/// placeholder'lari runtime'da gercek sirket degerleriyle replace eder. Veri kaynaklari:
///   - Company entity (Id, Name, Title, TaxNumber, TaxOffice)
///   - ErpConnectionSettings (Provider, Company, Business, Branch, Username)
///   - IntegrationApiProfile.AuthConfigJson icindeki extraFields (dbname, dbuser, branchcode, dbtype)
/// </summary>
public sealed class ScheduledTaskTokenResolver : IScheduledTaskTokenResolver
{
    private readonly ICompanyRepository _companyRepo;
    private readonly IErpConnectionSettingsRepository _erpRepo;
    private readonly IIntegrationApiProfileRepository _apiProfileRepo;

    public ScheduledTaskTokenResolver(
        ICompanyRepository companyRepo,
        IErpConnectionSettingsRepository erpRepo,
        IIntegrationApiProfileRepository apiProfileRepo)
    {
        _companyRepo = companyRepo;
        _erpRepo = erpRepo;
        _apiProfileRepo = apiProfileRepo;
    }

    public async Task<IReadOnlyDictionary<string, string?>> ResolveAsync(int companyId, CancellationToken cancellationToken)
    {
        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["COMPANY_ID"] = companyId.ToString(),
        };

        var company = await _companyRepo.GetByIdAsync(companyId, cancellationToken);
        if (company is not null)
        {
            tokens["COMPANY_NAME"]       = company.Name;
            tokens["COMPANY_TITLE"]      = company.Title;
            tokens["COMPANY_TAX_NO"]     = company.TaxNumber;
            tokens["COMPANY_TAX_OFFICE"] = company.TaxOffice;
        }

        var erpAll = await _erpRepo.GetAllAsync(cancellationToken);
        var erp = erpAll.FirstOrDefault(x => x.CompanyId == companyId && x.IsActive);
        if (erp is not null)
        {
            tokens["ERP_PROVIDER"] = erp.Provider;
            tokens["ERP_COMPANY"]  = erp.Company;
            tokens["ERP_BUSINESS"] = erp.Business;
            tokens["ERP_BRANCH"]   = erp.Branch;
            tokens["ERP_USERNAME"] = erp.Username;
        }

        var profiles = await _apiProfileRepo.GetByCompanyAsync(companyId, cancellationToken);
        var profile = profiles.FirstOrDefault(p => p.IsActive) ?? profiles.FirstOrDefault();
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.AuthConfigJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(profile.AuthConfigJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("extraFields", out var extraFields) &&
                    extraFields.ValueKind == JsonValueKind.Object)
                {
                    tokens["INTEGRATION_DB"]          = TryReadString(extraFields, "dbname");
                    tokens["INTEGRATION_DB_USER"]     = TryReadString(extraFields, "dbuser");
                    tokens["INTEGRATION_BRANCH_CODE"] = TryReadString(extraFields, "branchcode");
                    tokens["INTEGRATION_DB_TYPE"]     = TryReadString(extraFields, "dbtype");
                }
            }
            catch (JsonException) { /* sessiz: token'lar set edilmez, replace pas gecer */ }
        }

        return tokens;
    }

    private static string? TryReadString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            _                     => null,
        };
    }
}
