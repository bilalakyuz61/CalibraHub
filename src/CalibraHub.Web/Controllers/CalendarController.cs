using System.Security.Claims;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Calendar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
[Route("[controller]/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.Calendar)]
public sealed class CalendarController : Controller
{
    private readonly CalendarService _calendarService;

    public CalendarController(CalendarService calendarService)
    {
        _calendarService = calendarService;
    }

    /// <summary>GET /Calendar/Index — Tam sayfa takvim (sol menü "Genel → Takvim").</summary>
    [HttpGet]
    [Route("/Calendar")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Takvim";
        ViewData["FormCode"] = "";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Events(
        [FromQuery] string start, [FromQuery] string end, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Json(new { events = Array.Empty<object>() });

        var events = await _calendarService.GetEventsAsync(userId, start, end, ct);
        return Json(new { events });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEvent(
        [FromBody] SaveCalendarEventRequest req, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Json(new { ok = false, error = "Oturum bulunamadı" });

        var username = User.Identity?.Name ?? "";
        try
        {
            var id = await _calendarService.SaveEventAsync(userId, req, username, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEvent(
        [FromBody] DeleteCalendarEventRequest req, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Json(new { ok = false, error = "Oturum bulunamadı" });

        var username = User.Identity?.Name ?? "";
        try
        {
            await _calendarService.DeleteEventAsync(userId, req.Id, username, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }
}
