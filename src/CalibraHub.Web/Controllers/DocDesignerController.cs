using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class DocDesignerController : Controller
{
    private readonly IDocDesignerService _svc;

    public DocDesignerController(IDocDesignerService svc) => _svc = svc;

    private static readonly Dictionary<string, string> DocTypeLabels = new()
    {
        ["sales_quote"]    = "Satış Teklifi",
        ["sales_order"]    = "Satış Siparişi",
        ["purchase_order"] = "Satın Alma Siparişi",
        ["delivery_note"]  = "İrsaliye",
        ["invoice"]        = "Fatura",
        ["expense_note"]   = "Gider Pusulası",
        ["custom"]         = "Özel Belge",
    };

    private static readonly Dictionary<string, string> DocTypeColors = new()
    {
        ["sales_quote"]    = "indigo",
        ["sales_order"]    = "blue",
        ["purchase_order"] = "violet",
        ["delivery_note"]  = "emerald",
        ["invoice"]        = "amber",
        ["expense_note"]   = "rose",
        ["custom"]         = "slate",
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return View(config);
    }

    [HttpGet("BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return Json(config);
    }

    [HttpGet("New")]
    public IActionResult New() => View("Edit", 0);

    [HttpGet("Edit/{id:int}")]
    public IActionResult Edit(int id) => View(id);

    [HttpPost("DeleteJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteJson(int id, CancellationToken ct)
    {
        try
        {
            await _svc.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost("SetDefaultJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultJson(int id, CancellationToken ct)
    {
        try
        {
            await _svc.SetDefaultAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var layouts = await _svc.ListAsync(null, ct);
        var ordered = layouts.OrderByDescending(l => l.UpdatedAt).ToList();

        var entities = ordered.Select(l =>
        {
            var docLabel = DocTypeLabels.TryGetValue(l.DocType ?? "", out var lbl) ? lbl : (l.DocType ?? "—");
            var docColor = DocTypeColors.TryGetValue(l.DocType ?? "", out var col) ? col : "slate";

            return (object)new
            {
                id          = l.Id,
                title       = l.Name,
                subtitle    = (string?)null,
                description = l.Description ?? string.Empty,
                imageUrl    = (string?)null,
                statusBadge = l.IsDefault
                    ? new { label = "Varsayılan", color = "emerald" }
                    : (object?)null,
                widgets = new object[]
                {
                    new
                    {
                        id       = "w_doctype",
                        type     = "data",
                        dataType = "text",
                        label    = "Belge Tipi",
                        value    = docLabel,
                        detail   = (string?)null,
                        color    = docColor,
                    },
                    new
                    {
                        id       = "w_updated",
                        type     = "data",
                        dataType = "text",
                        label    = "Son Güncelleme",
                        value    = l.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                        detail   = (string?)null,
                        color    = "slate",
                    },
                },
                primaryAction = new
                {
                    label      = "Düzenle",
                    icon       = "Edit",
                    color      = "amber",
                    url        = $"/DocDesigner/Edit/{l.Id}",
                    hideButton = false,
                },
                secondaryAction = new
                {
                    label     = "Sil",
                    icon      = "Trash2",
                    apiUrl    = $"/DocDesigner/DeleteJson?id={l.Id}",
                    apiMethod = "POST",
                    confirm   = $"Şablonu silmek istediğinize emin misiniz? ({l.Name})",
                },
                extraActions = l.IsDefault
                    ? Array.Empty<object>()
                    : new object[]
                    {
                        new
                        {
                            type    = "api-post",
                            label   = "Varsayılan Yap",
                            icon    = "Star",
                            color   = "emerald",
                            url     = $"/DocDesigner/SetDefaultJson?id={l.Id}",
                            confirm = $"\"{l.Name}\" şablonu {docLabel} için varsayılan olarak ayarlansın mı?",
                        },
                    },
            };
        }).ToList();

        return new
        {
            boardKey          = "doc-designer-layouts",
            title             = "Belge Tasarımcısı",
            subtitle          = $"{entities.Count} şablon",
            icon              = "FileText",
            iconColor         = "indigo",
            refreshUrl        = "/DocDesigner/BoardConfig",
            searchPlaceholder = "Şablon ara… (ad, açıklama)",
            emptyText         = "Henüz şablon yok — \"Yeni Şablon\" ile başlayın",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Şablon", icon = "Plus", variant = "primary", url = "/DocDesigner/New" },
            },
            masterWidgets = Array.Empty<object>(),
            entities,
        };
    }
}
