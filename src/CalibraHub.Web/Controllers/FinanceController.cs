using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class FinanceController : Controller
{
    private readonly IFinanceService _financeService;
    private readonly IWidgetService _widgetService;

    private const int DefaultPageSize = 50;

    public FinanceController(IFinanceService financeService, IWidgetService widgetService)
    {
        _financeService = financeService;
        _widgetService = widgetService;
    }

    // GET /Finance/Contacts — sayfa hemen render, veri AJAX ile gelir
    public IActionResult Contacts()
    {
        return View(new ContactsViewModel { BoardConfig = null });
    }

    // GET /Finance/GetContactsBoardConfig — ilk yuklemede AJAX ile cagrilir
    [HttpGet]
    public async Task<IActionResult> GetContactsBoardConfig(CancellationToken ct)
    {
        var boardConfig = await BuildContactsBoardConfigAsync(null, 0, DefaultPageSize, ct);
        return Json(boardConfig);
    }

    // GET /Finance/GetContactsPage?page=1&pageSize=50&search=abc
    [HttpGet]
    public async Task<IActionResult> GetContactsPage(
        int page = 1, int pageSize = 50, string? search = null, byte? accountType = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 10) pageSize = 10;
        if (pageSize > 200) pageSize = 200;

        var offset = (page - 1) * pageSize;

        try
        {
            var (accounts, totalCount) = await _financeService.GetContactsPagedAsync(
                accountType, search, offset, pageSize, ct);

            var entities = await BuildEntitiesAsync(accounts, ct);

            return Json(new
            {
                entities,
                totalCount,
                page,
                pageSize,
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private async Task<object> BuildContactsBoardConfigAsync(
        string? search, int offset, int pageSize, CancellationToken ct)
    {
        var (accounts, totalCount) = await _financeService.GetContactsPagedAsync(
            null, search, offset, pageSize, ct);

        var masterWidgets = await BuildMasterWidgetsAsync(ct);
        var entities = await BuildEntitiesAsync(accounts, ct);

        return new
        {
            boardKey = "contact-accounts",
            title = "Cari Hesaplar",
            subtitle = $"{totalCount:N0} cari",
            icon = "Building2",
            iconColor = "cyan",
            searchPlaceholder = "Cari ara... (kod, unvan, vergi no)",
            emptyText = "Henuz cari hesap eklenmemis",
            apiUrl = "/Finance/GetContactsPage",
            totalCount,
            pageSize,
            actions = new[]
            {
                new
                {
                    id = "new",
                    label = "Yeni Cari",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Finance/ContactEdit",
                },
            },
            masterWidgets,
            entities,
        };
    }

    private async Task<List<object>> BuildMasterWidgetsAsync(CancellationToken ct)
    {
        var masterWidgets = new List<object>();
        var contactsSchema = await _widgetService.GetFormSchemaByCodeAsync("CONTACTS", ct);
        if (contactsSchema != null)
        {
            foreach (var w in contactsSchema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                masterWidgets.Add(new
                {
                    id           = w.WidgetCode,
                    dbId         = w.Id,
                    isPlainField = w.IsPlainField,
                    type         = "data",
                    dataType     = w.DataType.ToLowerInvariant(),
                    label        = w.Label,
                });
            }
        }
        return masterWidgets;
    }

    private async Task<List<object>> BuildEntitiesAsync(
        IReadOnlyCollection<ContactDto> accounts, CancellationToken ct)
    {
        var recordIds = accounts.Select(a => a.Id.ToString()).ToArray();
        var batchWidgets = recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("CONTACTS", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        var entities = new List<object>();
        foreach (var account in accounts)
        {
            var widgets = new List<object>();
            if (!string.IsNullOrWhiteSpace(account.Phone))
                widgets.Add(new { id = "sys_phone",  type = "data", dataType = "text", label = "Telefon",  value = account.Phone,      color = "blue"    });
            if (!string.IsNullOrWhiteSpace(account.Email))
                widgets.Add(new { id = "sys_email",  type = "data", dataType = "text", label = "E-Posta",  value = account.Email,      color = "cyan"    });
            if (!string.IsNullOrWhiteSpace(account.TaxNumber))
                widgets.Add(new { id = "sys_tax_no", type = "data", dataType = "text", label = "Vergi No", value = account.TaxNumber,  color = "slate"   });
            if (!string.IsNullOrWhiteSpace(account.City))
                widgets.Add(new { id = "sys_city",   type = "data", dataType = "text", label = "İl",       value = account.City,       color = "teal"    });
            if (!string.IsNullOrWhiteSpace(account.District))
                widgets.Add(new { id = "sys_district", type = "data", dataType = "text", label = "İlçe",   value = account.District,   color = "teal"    });
            widgets.Add(new { id = "sys_type", type = "data", dataType = "text", label = "Tip",
                value = account.AccountType == 1 ? "Musteri" : "Tedarikci",
                color = account.AccountType == 1 ? "emerald" : "violet" });

            var recordId = account.Id.ToString();
            if (batchWidgets.TryGetValue(recordId, out var renderDtos))
            {
                foreach (var w in renderDtos)
                {
                    widgets.Add(new {
                        id           = w.WidgetId,
                        type         = "data",
                        dataType     = w.DataType.ToLowerInvariant(),
                        label        = w.Label,
                        value        = w.Value,
                        isPlainField = w.IsPlainField,
                    });
                }
            }

            entities.Add(new
            {
                id = account.Id,
                title = string.IsNullOrWhiteSpace(account.AccountTitle) ? "(isimsiz)" : account.AccountTitle,
                subtitle = account.AccountCode ?? string.Empty,
                description = string.Join(" / ", new[] { account.City, account.District }.Where(s => !string.IsNullOrWhiteSpace(s))),
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Duzenle",
                    icon = "Edit",
                    url = $"/Finance/ContactEdit?id={account.Id}",
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Finance/DeleteContactJson?id={account.Id}",
                    confirm = $"Bu cari hesabi silmek istediginizden emin misiniz? ({account.AccountCode} — {account.AccountTitle})",
                },
            });
        }
        return entities;
    }

    // GET /Finance/ContactEdit
    public async Task<IActionResult> ContactEdit(int? id, CancellationToken cancellationToken)
    {
        ContactDto? existing = null;
        if (id.HasValue && id.Value > 0)
        {
            existing = await _financeService.GetContactByIdAsync(id.Value, cancellationToken);
        }

        ViewData["Title"] = existing is null ? "Yeni Cari Hesap" : $"Cari Düzenle — {existing.AccountCode}";
        ViewData["Contact"] = existing;

        return View();
    }

    // GET /Finance/GetContacts?accountType=1&search=abc
    [HttpGet]
    public async Task<IActionResult> GetContacts(
        byte? accountType, string? search, CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await _financeService.GetContactsAsync(accountType, search, cancellationToken);
            return Json(accounts);
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    // POST /Finance/UpsertContact
    [HttpPost]
    public async Task<IActionResult> UpsertContact(
        [FromBody] SaveContactRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
            return Json(new { success = false, message = "Geçersiz istek verisi." });
        try
        {
            var (success, error, account) = await _financeService.UpsertContactAsync(request, cancellationToken);
            if (!success)
                return Json(new { success = false, message = error });
            return Json(new { success = true, account });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatası: " + ex.Message });
        }
    }

    // POST /Finance/DeleteContact
    [HttpPost]
    public async Task<IActionResult> DeleteContact(
        [FromBody] DeleteContactBody? body, CancellationToken cancellationToken)
    {
        if (body is null)
            return Json(new { success = false, message = "Geçersiz istek verisi." });
        try
        {
            var (success, error) = await _financeService.DeleteContactAsync(body.Id, cancellationToken);
            if (!success)
                return Json(new { success = false, message = error });
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatası: " + ex.Message });
        }
    }

    /// <summary>
    /// SmartCard-uyumlu delete endpoint'i — query string ile id bekler.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DeleteContactJson(int id, CancellationToken ct)
    {
        try
        {
            var (success, error) = await _financeService.DeleteContactAsync(id, ct);
            if (!success)
                return Json(new { success = false, message = error });
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}
