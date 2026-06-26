using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Entegrasyon Doc Catalog admin — Provider, Enum Tanimlari, Alan Aciklamalari
/// SmartBoard sayfalari + edit ekranlari.
///
/// JSON API'lar `IntegrationsController`'da (`/Integrations/api/doc-catalog/...`).
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.Integrations)]
public sealed class IntegrationDocCatalogController : Controller
{
    private readonly IIntegrationDocCatalogService _svc;

    public IntegrationDocCatalogController(IIntegrationDocCatalogService svc)
    {
        _svc = svc;
    }

    // ── Enum Tanimlari ────────────────────────────────────────────────────

    [HttpGet("/IntegrationDocCatalog/Enums")]
    public IActionResult Enums()
    {
        // Standalone enum listesi kaldirildi — Entegrasyon Wizard'in "Enum Tanımları"
        // tab'i ayni SmartBoard'u mount eder ve kullanici wizard icinde kalir.
        // Eski URL'lere uyum: kalici 301 redirect yerine hash + Found (302) — tarayici cache'i.
        return Redirect("/Integrations#enums");
    }

    [HttpGet("/IntegrationDocCatalog/Enums/BoardEntities")]
    public async Task<IActionResult> EnumsBoardEntities(CancellationToken ct)
    {
        var items = await _svc.ListEnumsAsync(null, true, ct);
        var providers = await _svc.ListProvidersAsync(true, ct);
        return Json(BuildEnumsBoardConfig(items, providers));
    }

    [HttpGet("/IntegrationDocCatalog/EnumEdit")]
    public async Task<IActionResult> EnumEdit(int? id, CancellationToken ct)
    {
        var providers = await _svc.ListProvidersAsync(true, ct);
        if (!id.HasValue || id.Value <= 0)
            return View(new IntegrationDocCatalogEnumEditViewModel { Providers = providers });

        var item = await _svc.GetEnumAsync(id.Value, ct);
        if (item is null) return Redirect("/Integrations#enums");
        return View(new IntegrationDocCatalogEnumEditViewModel { Enum = item, Providers = providers });
    }

    // NOT: "Alan Aciklamalari" (FieldDocs) admin ekrani kaldirildi — yeni model:
    // tum field path eslesmesi IntegrationEnumDefinition.UsedInFieldPaths uzerinden
    // tek noktada (EnumEdit) yonetilir. /Integrations/api/field-docs runtime endpoint'i
    // hala geriye uyum icin eski FieldDoc kayitlarini + yeni enum-bazli sentezi merge eder.

    // ── Board config builders ─────────────────────────────────────────────

    private static object BuildEnumsBoardConfig(
        IReadOnlyList<IntegrationEnumDefinitionAdminDto> items,
        IReadOnlyList<IntegrationProviderAdminDto> providers)
    {
        var providerColors = providers.ToDictionary(p => p.Code, p => p.IconColor ?? "indigo");
        return new
        {
            boardKey   = "integration-doc-catalog-enums",
            title      = "Enum Tanımları",
            subtitle   = $"{items.Count} enum · {providers.Count} provider",
            icon       = "Sigma",
            iconColor  = "indigo",
            refreshUrl = "/IntegrationDocCatalog/Enums/BoardEntities",
            searchPlaceholder = "Kod / etiket / provider / endpoint / alan adı ara…",
            emptyText  = "Henüz enum tanımlanmamış.",
            actions    = new[]
            {
                new { id = "new", label = "Yeni Enum", icon = "Plus", variant = "primary", url = "/IntegrationDocCatalog/EnumEdit" }
            },
            masterWidgets = new List<object>
            {
                SmartBoardFilterHelpers.MakeStdWidget("w_count",  "Değer",    "numeric"),
                SmartBoardFilterHelpers.MakeStdWidget("w_paths",  "Kullanım", "numeric"),
                SmartBoardFilterHelpers.MakeStdWidget("w_active", "Durum",    "boolean"),
            },
            entities = items.Select(e =>
            {
                // 2026-05-21: Arama kapsamına alan adı (UsedInFields path'leri) eklendi —
                // SmartBoard client-side search artık title/subtitle/description + searchTags'i
                // tarayarak path/endpoint adına göre eşleştirir. searchTags UI'da gösterilmez.
                var searchTagBits = new List<string>();
                foreach (var u in e.UsedInFields)
                {
                    if (!string.IsNullOrWhiteSpace(u.Path)) searchTagBits.Add(u.Path);
                    if (u.EndpointId.HasValue) searchTagBits.Add("ep#" + u.EndpointId.Value);
                }
                if (!string.IsNullOrWhiteSpace(e.ProviderCode)) searchTagBits.Add(e.ProviderCode);
                var searchTags = string.Join(' ', searchTagBits);

                return new
                {
                id = e.Id,
                title = e.Code,
                subtitle = e.Label,
                description = e.Description ?? string.Empty,
                searchTags,
                statusBadge = new
                {
                    label = e.ProviderCode,
                    color = providerColors.GetValueOrDefault(e.ProviderCode, "indigo"),
                },
                widgets = new[]
                {
                    new { id = "w_count", type = "data", dataType = "numeric",
                          label = "Değer", value = e.Values.Count.ToString(), detail = "değer", color = "indigo" },
                    new { id = "w_paths", type = "data", dataType = "numeric",
                          label = "Kullanım", value = e.UsedInFields.Count.ToString(), detail = "alan",
                          color = e.UsedInFields.Count > 0 ? "blue" : "slate" },
                    new { id = "w_active", type = "data", dataType = "text",
                          label = "Durum", value = e.IsActive ? "Aktif" : "Pasif", detail = (string?)null,
                          color = e.IsActive ? "emerald" : "slate" },
                },
                primaryAction = new
                {
                    label = "Düzenle",
                    icon = "Edit",
                    color = "amber",
                    url = $"/IntegrationDocCatalog/EnumEdit?id={e.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Integrations/api/doc-catalog/enums/delete/{e.Id}",
                    apiMethod = "POST",
                    confirm = $"'{e.Code}' enum'unu pasife alalım mı?",
                },
                };
            }).ToArray(),
        };
    }

}

// View models
public sealed class IntegrationDocCatalogEnumsViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class IntegrationDocCatalogEnumEditViewModel
{
    public IntegrationEnumDefinitionAdminDto? Enum { get; init; }
    public IReadOnlyList<IntegrationProviderAdminDto> Providers { get; init; } = Array.Empty<IntegrationProviderAdminDto>();
}

