using CalibraHub.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ProjectInstructionsJsonController — Proje talimatlari (PROJECT_INSTRUCTIONS.txt
/// + PROJECT_REQUESTS.json) JSON read/write endpoint'leri
/// (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/GetProjectInstructionsJson    → content + tamamlanmis istekler
///   - POST /Admin/SaveProjectInstructionsJson   → content kaydet
///
/// AdminController'da kalan: ProjectInstructions view + SaveProjectInstructions
/// form-post (BuildProjectInstructionsViewModelAsync helper'a bagli).
/// </summary>
[Authorize]
public sealed class ProjectInstructionsJsonController : Controller
{
    private readonly IWebHostEnvironment _webHostEnvironment;

    public ProjectInstructionsJsonController(IWebHostEnvironment webHostEnvironment)
    {
        _webHostEnvironment = webHostEnvironment;
    }

    [HttpGet("/Admin/GetProjectInstructionsJson")]
    public async Task<IActionResult> GetProjectInstructionsJson(CancellationToken cancellationToken)
    {
        var filePath = ResolveProjectInstructionsPath();
        var content = System.IO.File.Exists(filePath)
            ? await System.IO.File.ReadAllTextAsync(filePath, cancellationToken)
            : string.Empty;
        var requestItems = await LoadProjectRequestItemsAsync(cancellationToken);
        return Json(new
        {
            content,
            completedItems = requestItems
                .Where(x => x.IsCompleted)
                .Select(x => new { x.RequestId, x.Category, x.Title, x.UserComment })
                .ToArray()
        });
    }

    [HttpPost("/Admin/SaveProjectInstructionsJson")]
    public async Task<IActionResult> SaveProjectInstructionsJson([FromBody] ProjectInstructionsJsonInput input, CancellationToken cancellationToken)
    {
        var normalizedContent = input.Content ?? string.Empty;
        var filePath = ResolveProjectInstructionsPath();
        await System.IO.File.WriteAllTextAsync(filePath, normalizedContent, new UTF8Encoding(false), cancellationToken);
        return Json(new { success = true, message = "Talimat dosyasi kaydedildi." });
    }

    // ── Helpers (AdminController'dakilerin kopyasi — sonraki refactor'da shared service'e) ──
    private string ResolveProjectInstructionsPath() =>
        ResolveProjectRootFilePath("PROJECT_INSTRUCTIONS.txt");

    private string ResolveProjectRequestsPath() =>
        ResolveProjectRootFilePath("PROJECT_REQUESTS.json");

    private string ResolveProjectRootFilePath(string fileName)
    {
        var currentDirectory = new DirectoryInfo(_webHostEnvironment.ContentRootPath);

        while (currentDirectory is not null)
        {
            var candidatePath = Path.Combine(currentDirectory.FullName, fileName);
            if (System.IO.File.Exists(candidatePath))
                return candidatePath;

            if (currentDirectory.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any())
                return candidatePath;

            currentDirectory = currentDirectory.Parent;
        }

        return Path.Combine(_webHostEnvironment.ContentRootPath, fileName);
    }

    private async Task<List<ProjectInstructionRequestItemInput>> LoadProjectRequestItemsAsync(CancellationToken cancellationToken)
    {
        var filePath = ResolveProjectRequestsPath();
        if (!System.IO.File.Exists(filePath)) return new List<ProjectInstructionRequestItemInput>();

        try
        {
            var content = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(content)) return new List<ProjectInstructionRequestItemInput>();

            var document = JsonSerializer.Deserialize<ProjectInstructionRequestDocument>(content);
            return (document?.Items ?? Enumerable.Empty<ProjectInstructionRequestItemInput>())
                .Select(item => new ProjectInstructionRequestItemInput
                {
                    RequestId = item.RequestId?.Trim() ?? string.Empty,
                    Category = item.Category?.Trim() ?? string.Empty,
                    Title = item.Title?.Trim() ?? string.Empty,
                    Description = item.Description?.Trim() ?? string.Empty,
                    IsCompleted = item.IsCompleted,
                    UserComment = item.UserComment?.Trim() ?? string.Empty
                })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.RequestId) ||
                    !string.IsNullOrWhiteSpace(item.Category) ||
                    !string.IsNullOrWhiteSpace(item.Title) ||
                    !string.IsNullOrWhiteSpace(item.Description) ||
                    !string.IsNullOrWhiteSpace(item.UserComment))
                .ToList();
        }
        catch (JsonException)
        {
            return new List<ProjectInstructionRequestItemInput>();
        }
    }

    private sealed class ProjectInstructionRequestDocument
    {
        public List<ProjectInstructionRequestItemInput>? Items { get; set; }
    }
}
