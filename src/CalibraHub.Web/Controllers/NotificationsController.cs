using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Shell navbar'daki bell dropdown'a veri sunan endpoint'ler. Bildirimler
/// uygulamada ReminderNotificationWorker ve diger servisler tarafindan olusturulur.
/// </summary>
[Authorize]
[Route("[controller]/[action]")]
public sealed class NotificationsController : Controller
{
    private readonly IUserNotificationRepository _repo;

    public NotificationsController(IUserNotificationRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public async Task<IActionResult> ListJson(int take = 30, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId <= 0) return Json(new { items = Array.Empty<object>(), unreadCount = 0 });

        var items = await _repo.GetRecentAsync(userId, take, ct);
        var unread = await _repo.GetUnreadCountAsync(userId, ct);
        return Json(new
        {
            unreadCount = unread,
            items = items.Select(n => new
            {
                id         = n.Id,
                title      = n.Title,
                body       = n.Body,
                sourceType = n.SourceType,
                sourceId   = n.SourceId,
                link       = n.Link,
                isRead     = n.IsRead,
                createdAt  = n.Created.ToString("yyyy-MM-ddTHH:mm:ss"),
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> UnreadCountJson(CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId <= 0) return Json(new { unreadCount = 0 });
        var unread = await _repo.GetUnreadCountAsync(userId, ct);
        return Json(new { unreadCount = unread });
    }

    public sealed class IdInput { public int Id { get; set; } }

    [HttpPost]
    public async Task<IActionResult> MarkReadJson([FromBody] IdInput input, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId <= 0) return Json(new { success = false });
        await _repo.MarkReadAsync(input.Id, userId, DateTime.Now, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllReadJson(CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId <= 0) return Json(new { success = false });
        await _repo.MarkAllReadAsync(userId, DateTime.Now, ct);
        return Json(new { success = true });
    }

    private int CurrentUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        return int.TryParse(s, out var i) ? i : 0;
    }
}
