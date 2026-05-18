using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// FormManagementController — Form Tasarim Ayarlari (SmartBoard) ve form
/// JSON CRUD endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/FormManagement     → SmartBoard view
///   - GET  /Admin/FormsBoardConfig   → SmartBoard refresh JSON
///   - POST /Admin/DeleteFormJson?id= → form sil
///   - GET  /Admin/FormEdit?id=       → edit view
/// </summary>
[Authorize]
public sealed class FormManagementController : Controller
{
    private readonly IFormRepository _formRepository;

    private static readonly JsonSerializerOptions BoardConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public FormManagementController(IFormRepository formRepository)
    {
        _formRepository = formRepository;
    }

    [HttpGet("/Admin/FormManagement")]
    public async Task<IActionResult> FormManagement(CancellationToken ct)
    {
        ViewData["AdminMenu"] = "form-management";
        var config = await BuildFormsBoardConfigAsync(ct);
        return View("~/Views/Admin/FormManagement.cshtml", new FormsSmartBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Admin/FormsBoardConfig")]
    public async Task<IActionResult> FormsBoardConfig(CancellationToken ct)
    {
        var config = await BuildFormsBoardConfigAsync(ct);
        return Json(config, BoardConfigJsonOptions);
    }

    [HttpPost("/Admin/DeleteFormJson")]
    public async Task<IActionResult> DeleteFormJson(int id, CancellationToken ct)
    {
        try
        {
            await _formRepository.DeleteAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("/Admin/FormEdit")]
    public async Task<IActionResult> FormEdit(int? id, CancellationToken ct)
    {
        ViewData["AdminMenu"] = "form-management";
        if (id.HasValue)
        {
            var form = await _formRepository.GetByIdAsync(id.Value, ct);
            if (form is null) return NotFound();
            return View("~/Views/Admin/FormEdit.cshtml", new FormEditViewModel
            {
                Id            = form.Id,
                FormCode      = form.FormCode,
                FormName      = form.FormName,
                Module        = form.Module,
                SubModule     = form.SubModule,
                SortOrder     = form.SortOrder,
                IsActive      = form.IsActive,
                BaseTable     = form.BaseTable,
                BaseRecordKey = form.BaseRecordKey,
            });
        }
        return View("~/Views/Admin/FormEdit.cshtml", new FormEditViewModel { IsActive = true });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private async Task<object> BuildFormsBoardConfigAsync(CancellationToken ct)
    {
        var forms = await _formRepository.GetAllAsync(ct);

        var entities = forms.Select(f => (object)new
        {
            id          = f.Id,
            title       = f.FormName,
            subtitle    = f.FormCode,
            description = f.Module != null
                ? f.Module + (f.SubModule != null ? " / " + f.SubModule : string.Empty)
                : (string?)null,
            module      = f.Module,
            subModule   = f.SubModule,
            sortOrder   = f.SortOrder,
            imageUrl    = (string?)null,
            statusBadge = new { label = f.IsActive ? "Aktif" : "Pasif", color = f.IsActive ? "emerald" : "slate" },
            widgets     = BuildFormWidgets(f),
            primaryAction = new
            {
                label      = "Düzenle",
                icon       = "Edit",
                color      = "amber",
                url        = $"/Admin/FormEdit?id={f.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil",
                icon      = "Trash2",
                apiUrl    = $"/Admin/DeleteFormJson?id={f.Id}",
                apiMethod = "POST",
                confirm   = $"Bu form tanımını silmek istediğinize emin misiniz? ({f.FormName})",
            },
        }).ToList();

        return new
        {
            boardKey          = "admin-forms",
            title             = "Form Tasarım Ayarları",
            subtitle          = $"{entities.Count} form",
            icon              = "LayoutGrid",
            iconColor         = "indigo",
            refreshUrl        = "/Admin/FormsBoardConfig",
            searchPlaceholder = "Form kodu, adı, modül veya tablo ara…",
            emptyText         = "Henüz form tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Form", icon = "Plus", variant = "primary", url = "/Admin/FormEdit" },
            },
            masterWidgets = Array.Empty<object>(),
            entities,
        };
    }

    private static List<object> BuildFormWidgets(FormDto f)
    {
        var widgets = new List<object>();

        if (!string.IsNullOrWhiteSpace(f.BaseTable))
            widgets.Add(new { id = "w_table", type = "data", dataType = "text",
                label = "Tablo", value = f.BaseTable, detail = (string?)null, color = "indigo" });

        if (!string.IsNullOrWhiteSpace(f.BaseRecordKey))
            widgets.Add(new { id = "w_key", type = "data", dataType = "text",
                label = "Anahtar", value = f.BaseRecordKey, detail = (string?)null, color = "emerald" });

        widgets.Add(new { id = "w_sort", type = "data", dataType = "numeric",
            label = "Sıra", value = f.SortOrder.ToString(), detail = (string?)null, color = "slate" });

        return widgets;
    }
}
