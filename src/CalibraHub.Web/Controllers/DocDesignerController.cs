using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
[Route("[controller]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.DocTemplates)]
public sealed class DocDesignerController : Controller
{
    private readonly IDocDesignerService _svc;

    public DocDesignerController(IDocDesignerService svc) => _svc = svc;

    // 2026-06-03: Eski İngilizce kodlar (sales_quote vb.) backward-compat için tutulur;
    // canlı DocType.code değerleri Türkçe snake_case (satis_teklifi, alis_*, vb.).
    // Hepsini map'e ekleriz — eşleşmeyen sadece çok eski custom layout'lar olur.
    private static readonly Dictionary<string, string> DocTypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // Canlı kodlar
        ["satis_teklifi"]     = "Satış Teklifi",
        ["satis_siparisi"]    = "Satış Siparişi",
        ["alis_talebi"]       = "İhtiyaç Kaydı",
        ["alis_teklifi"]      = "Satın Alma Teklifi",
        ["alis_siparisi"]     = "Satın Alma Siparişi",
        ["satin_alma_talebi"] = "Satın Alma Talebi",
        ["fatura"]            = "Fatura",
        ["irsaliye"]          = "İrsaliye",
        ["urun_barkodu"]      = "Ürün Barkodu",
        ["raf_etiketi"]       = "Raf Etiketi",
        ["is_emri"]           = "İş Emri",
        ["mail_template"]     = "Mail Şablonu",
        ["zimmet_teslim"]     = "Zimmet Teslim Formu",
        ["arge_proje"]        = "AR-GE Projesi",
        // Legacy İngilizce kodlar (backward-compat)
        ["sales_quote"]       = "Satış Teklifi",
        ["sales_order"]       = "Satış Siparişi",
        ["purchase_order"]    = "Satın Alma Siparişi",
        ["delivery_note"]     = "İrsaliye",
        ["invoice"]           = "Fatura",
        ["expense_note"]      = "Gider Pusulası",
        ["custom"]            = "Özel Belge",
    };

    private static readonly Dictionary<string, string> DocTypeColors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Canlı kodlar
        ["satis_teklifi"]     = "indigo",
        ["satis_siparisi"]    = "blue",
        ["alis_talebi"]       = "amber",
        ["alis_teklifi"]      = "violet",
        ["alis_siparisi"]     = "violet",
        ["satin_alma_talebi"] = "rose",
        ["fatura"]            = "emerald",
        ["irsaliye"]          = "blue",
        ["urun_barkodu"]      = "slate",
        ["raf_etiketi"]       = "slate",
        ["is_emri"]           = "amber",
        ["mail_template"]     = "rose",
        ["zimmet_teslim"]     = "emerald",
        ["arge_proje"]        = "violet",
        // Legacy
        ["sales_quote"]       = "indigo",
        ["sales_order"]       = "blue",
        ["purchase_order"]    = "violet",
        ["delivery_note"]     = "emerald",
        ["invoice"]           = "amber",
        ["expense_note"]      = "rose",
        ["custom"]            = "slate",
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

    /// <summary>
    /// 2026-05-30 — Tam sayfa onizleme. Workspace tab'inda iframe ile yuklenir;
    /// kullanici Ctrl+P ile direkt yazdirabilir. previewLayout JSON yerine
    /// HTML content dondurur (text/html), tarayicinin native render'i devreye girer.
    /// </summary>
    [HttpGet("Preview/{id:int}")]
    public async Task<IActionResult> Preview(int id, CancellationToken ct)
    {
        try
        {
            var html = await _svc.RenderHtmlPreviewAsync(
                new DocLayoutRunRequest(id, null, null), ct);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            var msg = System.Net.WebUtility.HtmlEncode("İşlem sırasında bir hata oluştu.");
            var errorHtml = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Önizleme Hatası</title>"
                          + "<style>body{font-family:system-ui,sans-serif;padding:40px;color:#dc2626;background:#fef2f2;}"
                          + "h1{font-size:18px;}pre{white-space:pre-wrap;background:#fff;padding:14px;border-radius:6px;border:1px solid #fca5a5;}</style>"
                          + "</head><body><h1>Önizleme oluşturulamadı</h1><pre>" + msg + "</pre></body></html>";
            return Content(errorHtml, "text/html; charset=utf-8");
        }
    }

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
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
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
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var layouts = await _svc.ListAsync(null, ct);
        var ordered = layouts.OrderByDescending(l => l.UpdatedAt).ToList();
        var docTypeOptions = SmartBoardFilterHelpers.ToOptionsList(DocTypeLabels.Values.Distinct());
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeOptionsWidget("w_doctype", "Belge Tipi",     docTypeOptions),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_updated", "Son Güncelleme", "text"),
        };

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
                        dataType = "options",
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
            masterWidgets,
            entities,
        };
    }
}
