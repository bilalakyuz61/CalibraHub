using CalibraHub.Web.Infrastructure.Collaboration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// LocksController — Aktif Kilitler ekrani + SmartBoard refresh (rapor §2.3
/// AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/Locks                → view
///   - GET  /Admin/LocksBoardConfig     → board refresh JSON
/// </summary>
[Authorize]
public sealed class LocksController : Controller
{
    private readonly CollaborationRuntimeStore _collaborationStore;

    private static readonly JsonSerializerOptions BoardConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public LocksController(CollaborationRuntimeStore collaborationStore)
    {
        _collaborationStore = collaborationStore;
    }

    [HttpGet("/Admin/Locks")]
    public IActionResult Locks()
    {
        ViewData["AdminMenu"] = "locks";
        var activeLocks = _collaborationStore.GetAllActiveLocks(DateTime.Now);
        var boardConfig = BuildLocksBoardConfig(activeLocks);
        var boardConfigJson = JsonSerializer.Serialize(boardConfig, BoardConfigJsonOptions);
        ViewData["Title"] = "Aktif Kilitler";
        return View("~/Views/Admin/Locks.cshtml", (object)boardConfigJson);
    }

    [HttpGet("/Admin/LocksBoardConfig")]
    public IActionResult LocksBoardConfig()
    {
        var activeLocks = _collaborationStore.GetAllActiveLocks(DateTime.Now);
        return Json(BuildLocksBoardConfig(activeLocks));
    }

    private object BuildLocksBoardConfig(IReadOnlyCollection<CollaborationLockSnapshot> locks)
    {
        var entities = locks.Select(lk => new
        {
            id = $"{lk.RecordType}::{lk.RecordId}",
            title = string.IsNullOrWhiteSpace(lk.RecordTitle) ? $"{lk.RecordType} / {lk.RecordId}" : lk.RecordTitle,
            subtitle = lk.OwnerDisplayName,
            description = lk.PageUrl,
            statusBadge = new { label = "Kilitli", color = "rose" },
            widgets = new object[]
            {
                new { id = "w_type", type = "data", dataType = "text", label = "Modül",         value = lk.RecordType,                                                color = "indigo" },
                new { id = "w_rec",  type = "data", dataType = "text", label = "Kayıt ID",      value = lk.RecordId,                                                  color = "slate"  },
                new { id = "w_acq",  type = "data", dataType = "text", label = "Kilit Zamanı",   value = lk.AcquiredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), color = "amber"  },
                new { id = "w_hb",   type = "data", dataType = "text", label = "Son Heartbeat", value = lk.LastHeartbeatAt.ToLocalTime().ToString("HH:mm:ss"),       color = "slate"  },
            },
            secondaryAction = new
            {
                label     = "Kilidi Kır",
                icon      = "Unlock",
                apiUrl    = "/api/collaboration/break-lock",
                apiMethod = "POST",
                apiBody   = $"{{\"recordType\":\"{lk.RecordType}\",\"recordId\":\"{lk.RecordId}\"}}",
                confirm   = $"'{lk.RecordTitle ?? lk.RecordId}' kaydının kilidi kırılsın mı? ({lk.OwnerDisplayName} düzenliyordu)",
            },
        }).ToArray();

        return new
        {
            boardKey          = "admin-collaboration-locks",
            title             = "Aktif Kilitler",
            subtitle          = $"{locks.Count} aktif kilit",
            icon              = "Lock",
            iconColor         = "rose",
            refreshUrl        = "/Admin/LocksBoardConfig",
            searchPlaceholder = "Kullanıcı veya kayıt ara…",
            emptyText         = "Şu an aktif kilit bulunmuyor",
            actions           = Array.Empty<object>(),
            masterWidgets     = Array.Empty<object>(),
            entities,
        };
    }
}
