using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Faz D Adim 1 â€” Legacy dinamik alan â†’ yeni EAV tablolarina tek seferlik
/// migration endpoint'i.
///
/// Endpoint idempotent: tekrar calistirilmasi guvenli (mevcut kayitlar
/// overwrite edilmez, sadece eksikler eklenir).
/// </summary>
[Authorize]
[PermissionScope(FormCodes.SetupDefinitions)]
[ApiController]
[Route("api/legacy-migration")]
[IgnoreAntiforgeryToken]
public sealed class LegacyMigrationController : ControllerBase
{
    private readonly ILegacyMigrationService _migrationService;

    public LegacyMigrationController(ILegacyMigrationService migrationService)
    {
        _migrationService = migrationService;
    }

    // POST /api/legacy-migration/run
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        try
        {
            var report = await _migrationService.MigrateAsync(ct);
            return Ok(new
            {
                success = true,
                stats = new
                {
                    groupsMigrated = report.GroupsMigrated,
                    groupsSkipped  = report.GroupsSkipped,
                    fieldsMigrated = report.FieldsMigrated,
                    fieldsSkipped  = report.FieldsSkipped,
                    valuesMigrated = report.ValuesMigrated,
                    valuesSkipped  = report.ValuesSkipped,
                    warningsCount  = report.Warnings.Count,
                },
                warnings = report.Warnings,
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "İşlem sırasında bir hata oluştu.", stack = ex.StackTrace });
        }
    }
}
