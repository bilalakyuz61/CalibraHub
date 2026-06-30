using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Admin SQL kütüphanesi — onay akışı Karar (Decision) node'larında kullanılan
/// named query'lerin CRUD'u + designer'dan çağrılan Validate / TestExecute uçları.
///
/// Route prefix: /Admin/[action] — view path Views/ApprovalSqlQuery/{Action}.cshtml.
/// </summary>
[Authorize]
[Route("/Admin/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.ApprovalFlows)]
public sealed class ApprovalSqlQueryController : Controller
{
    private readonly IApprovalSqlQueryService _service;

    public ApprovalSqlQueryController(IApprovalSqlQueryService service)
    {
        _service = service;
    }

    // ── React board sayfası ──────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> SqlQueryLibrary(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        ViewData["Title"] = "SQL Sorgu Kütüphanesi";
        ViewData["BoardConfigJson"] = JsonSerializer.Serialize(config,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return View();
    }

    // ── In-place refresh — SmartBoard refreshUrl ─────────────────────────────
    [HttpGet("/Admin/SqlQueryLibrary/Config")]
    public async Task<IActionResult> Config(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return Json(config,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ── CRUD: kaydet ─────────────────────────────────────────────────────────
    [HttpPost("/Admin/SqlQueryLibrary/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveApprovalSqlQueryRequest req, CancellationToken ct)
    {
        try
        {
            var uid = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var _uid) ? _uid : (int?)null;
            var id = await _service.SaveAsync(req, uid, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ── CRUD: sil ────────────────────────────────────────────────────────────
    [HttpPost("/Admin/SqlQueryLibrary/Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ── Designer'dan SQL doğrulama ───────────────────────────────────────────
    [HttpPost("/Admin/SqlQueryLibrary/Validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate([FromBody] ValidateSqlRequest req, CancellationToken ct)
    {
        _ = ct; // ValidateSqlAsync senkron iş — ct kullanılmıyor
        var (ok, err) = await _service.ValidateSqlAsync(req?.Sql ?? string.Empty);
        return Json(new { ok, error = err });
    }

    // ── Designer'dan sample params ile çalıştır ──────────────────────────────
    [HttpPost("/Admin/SqlQueryLibrary/TestExecute")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestExecute([FromBody] ExecuteApprovalSqlRequest req, CancellationToken ct)
    {
        string? sql = req.SqlText;
        if (req.QueryId is int qid && qid > 0)
        {
            var entity = await _service.GetByIdAsync(qid, ct);
            if (entity is null) return Json(new ExecuteApprovalSqlResult(false, null, "Sorgu bulunamadı.", 0));
            sql = entity.SqlText;
        }

        var result = await _service.ExecuteAsync(sql, req.Parameters, ct);
        return Json(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var queries = await _service.GetAllAsync(ct);
        var entities = queries.Select(q => (object)new
        {
            id          = q.Id,
            title       = q.Name,
            subtitle    = ResultTypeLabel(q.ResultType),
            description = q.Description,
            imageUrl    = (string?)null,
            statusBadge = q.IsActive
                ? new { label = "Aktif", color = "emerald" }
                : (object)new { label = "Pasif", color = "slate" },
            widgets = new object[]
            {
                new { id = "w_resultType", type = "data", dataType = "options", label = "Sonuç Tipi", value = ResultTypeLabel(q.ResultType), color = "indigo" },
                new { id = "w_createdBy",  type = "data", dataType = "text",    label = "Oluşturan",  value = q.CreatedById?.ToString() ?? "—", color = "slate"  },
                new { id = "w_created",    type = "data", dataType = "text",    label = "Tarih",      value = q.Created.ToString("dd.MM.yyyy"), color = "blue" },
            },
            primaryAction = new
            {
                label      = "Düzenle",
                icon       = "Edit",
                color      = "amber",
                url        = $"/Admin/SqlQueryLibrary?id={q.Id}",
                hideButton = false,
            },
            secondaryAction = new
            {
                label     = "Sil",
                icon      = "Trash2",
                apiUrl    = $"/Admin/SqlQueryLibrary/Delete/{q.Id}",
                apiMethod = "POST",
                confirm   = $"Silmek istediğinize emin misiniz? ({q.Name})",
            },
        }).ToArray();

        return new
        {
            boardKey          = "admin-sql-query-library",
            title             = "SQL Sorgu Kütüphanesi",
            subtitle          = $"{entities.Length} sorgu",
            icon              = "Database",
            iconColor         = "indigo",
            refreshUrl        = "/Admin/SqlQueryLibrary/Config",
            searchPlaceholder = "Sorgu ara…",
            emptyText         = "Henüz SQL sorgusu tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Sorgu", icon = "Plus", variant = "primary", url = "/Admin/SqlQueryLibrary?id=0" },
            },
            entities,
        };
    }

    private static string ResultTypeLabel(string raw) => (raw ?? "scalar").ToLowerInvariant() switch
    {
        "boolean" => "Boolean",
        "count"   => "Count",
        _         => "Scalar",
    };

    /// <summary>Validate endpoint için minimal request DTO — { sql: "..." }.</summary>
    public sealed record ValidateSqlRequest(string Sql);
}
