using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
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
public sealed class IntegrationDocCatalogController : Controller
{
    private readonly IIntegrationDocCatalogService _svc;

    public IntegrationDocCatalogController(IIntegrationDocCatalogService svc)
    {
        _svc = svc;
    }

    // ── Enum Tanimlari ────────────────────────────────────────────────────

    [HttpGet("/IntegrationDocCatalog/Enums")]
    public async Task<IActionResult> Enums(CancellationToken ct)
    {
        var items = await _svc.ListEnumsAsync(providerId: null, includeInactive: true, ct);
        var providers = await _svc.ListProvidersAsync(includeInactive: true, ct);
        return View(new IntegrationDocCatalogEnumsViewModel
        {
            BoardConfig = BuildEnumsBoardConfig(items, providers),
        });
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
        if (item is null) return RedirectToAction(nameof(Enums));
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
            searchPlaceholder = "Kod / etiket / provider ara…",
            emptyText  = "Henüz enum tanımlanmamış.",
            actions    = new[]
            {
                new { id = "new", label = "Yeni Enum", icon = "Plus", variant = "primary", url = "/IntegrationDocCatalog/EnumEdit" }
            },
            entities = items.Select(e => new
            {
                id = e.Id,
                title = e.Code,
                subtitle = e.Label,
                description = e.Description ?? string.Empty,
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
                          label = "Kullanım", value = e.UsedInFieldPaths.Count.ToString(), detail = "alan",
                          color = e.UsedInFieldPaths.Count > 0 ? "blue" : "slate" },
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

