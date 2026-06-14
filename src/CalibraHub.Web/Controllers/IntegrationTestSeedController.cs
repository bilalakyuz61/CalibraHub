using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// SADECE GELISTIRME ICIN — E2E smoke test seed endpoint'i.
///
/// `POST /Integration/Test/SeedHttpBinEcho` çağrıldığında otomatik olarak:
///   1. Bir ApiProfile (httpbin.org base) olusturur
///   2. Bir IntegrationEndpoint (POST /post) olusturur
///   3. Bir Integration olusturur (SALES_QUOTE_EDIT formundan ornek alanlarla)
///   4. Birkac IntegrationMapping satiri olusturur (FormField + Constant + Formula)
///
/// Sonuc: caller'a integrationId doner, bunu /Integration/Run/{id} ile tetikleyip
/// HTTP çağrısı yapilabilir.
///
/// Sprint 2'de wizard UI tamamlandiginda BU CONTROLLER SILINIR.
/// </summary>
[Authorize]
public sealed class IntegrationTestSeedController : Controller
{
    [HttpPost("/Integration/Test/SeedHttpBinEcho")]
    public async Task<IActionResult> SeedHttpBinEcho(
        [FromServices] IIntegrationRepository integrationRepo,
        [FromServices] IIntegrationApiProfileRepository apiProfileRepo,
        CancellationToken ct)
    {
        try
        {
            // 1) ApiProfile
            var existing = await apiProfileRepo.GetByCompanyAsync(1, ct);
            var profile = existing.FirstOrDefault(p => p.Name == "TEST_HttpBin");
            if (profile is null)
            {
                profile = new IntegrationApiProfile
                {
                    Id = Guid.NewGuid(),
                    CompanyId = 1,
                    Name = "TEST_HttpBin",
                    AuthType = "None",
                    BaseUrl = "https://httpbin.org",
                    IsActive = true,
                };
                await apiProfileRepo.UpsertAsync(profile, ct);
            }

            // 2) Endpoint
            var endpointId = await integrationRepo.AddEndpointAsync(new IntegrationEndpoint
            {
                ApiProfileId = profile.Id,
                Name = "HttpBin Echo Test",
                HttpMethod = "POST",
                UrlTemplate = "/post",
                BodySchema = "{ \"belgeNo\": \"\", \"cariKod\": \"\", \"tutar\": 0 }",
                Description = "E2E smoke test echo endpoint (httpbin.org echolar request body'sini)",
                IsActive = true,
                // Test seed — CreatedById = null.
            }, ct);

            // 3) Integration
            var integrationId = await integrationRepo.AddAsync(new Integration
            {
                Name = "TEST: Satis Teklifi → HttpBin",
                Description = "E2E smoke test entegrasyonu",
                SourceFormCode = "SALES_QUOTE_EDIT",
                TargetEndpointId = endpointId,
                ErrorBehavior = IntegrationErrorBehavior.Skip,
                IsActive = true,
                // Test seed — CreatedById = null.
            }, ct);

            // 4) Mapping kurallari — basit ornekler
            var mappings = new List<IntegrationMapping>
            {
                // FormField: DocumentNumber → belgeNo
                new()
                {
                    IntegrationId = integrationId,
                    TargetPath = "belgeNo",
                    TargetDataType = "string",
                    SourceType = IntegrationSourceType.FormField,
                    SourceValue = "DocumentNumber",
                    IsRequired = true,
                    SortOrder = 1,
                },
                // Constant: TIPI alanina sabit "SatisTeklifi"
                new()
                {
                    IntegrationId = integrationId,
                    TargetPath = "tipi",
                    TargetDataType = "string",
                    SourceType = IntegrationSourceType.Constant,
                    SourceValue = "SatisTeklifi",
                    SortOrder = 2,
                },
                // Formula: GrandTotal'i 1.20 ile carp (KDV-dahil ornegi)
                new()
                {
                    IntegrationId = integrationId,
                    TargetPath = "tutar",
                    TargetDataType = "decimal",
                    FormatPattern = "F2",
                    SourceType = IntegrationSourceType.Formula,
                    SourceValue = "GrandTotal * 1.20",
                    SortOrder = 3,
                },
                // Nested path: FatUst.Tarih → DocumentDate
                new()
                {
                    IntegrationId = integrationId,
                    TargetPath = "fatUst.tarih",
                    TargetDataType = "datetime",
                    FormatPattern = "yyyy-MM-dd",
                    SourceType = IntegrationSourceType.FormField,
                    SourceValue = "DocumentDate",
                    SortOrder = 4,
                },
            };
            await integrationRepo.ReplaceMappingsAsync(integrationId, mappings, ct);

            return Json(new
            {
                success = true,
                profileId = profile.Id,
                endpointId,
                integrationId,
                message = $"Seed tamam. Test icin: POST /Integration/Run/{integrationId}",
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }
}
