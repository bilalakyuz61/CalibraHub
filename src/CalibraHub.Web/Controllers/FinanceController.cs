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

    // GET /Finance/ContactAccounts — ilk sayfa ile SmartBoard
    public async Task<IActionResult> ContactAccounts(CancellationToken ct)
    {
        var boardConfig = await BuildContactAccountsBoardConfigAsync(null, 0, DefaultPageSize, ct);
        return View(new ContactAccountsViewModel { BoardConfig = boardConfig });
    }

    // GET /Finance/GetContactAccountsPage?page=1&pageSize=50&search=abc
    [HttpGet]
    public async Task<IActionResult> GetContactAccountsPage(
        int page = 1, int pageSize = 50, string? search = null, byte? accountType = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 10) pageSize = 10;
        if (pageSize > 200) pageSize = 200;

        var offset = (page - 1) * pageSize;

        try
        {
            var (accounts, totalCount) = await _financeService.GetContactAccountsPagedAsync(
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

    private async Task<object> BuildContactAccountsBoardConfigAsync(
        string? search, int offset, int pageSize, CancellationToken ct)
    {
        var (accounts, totalCount) = await _financeService.GetContactAccountsPagedAsync(
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
            apiUrl = "/Finance/GetContactAccountsPage",
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
                    url = "/Finance/ContactAccountEdit",
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
        IReadOnlyCollection<ContactAccountDto> accounts, CancellationToken ct)
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
                widgets.Add(new { id = "sys_city",   type = "data", dataType = "text", label = "Sehir",    value = account.City,       color = "teal"    });
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
                description = account.Address ?? string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Duzenle",
                    icon = "Edit",
                    url = $"/Finance/ContactAccountEdit?id={account.Id}",
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Finance/DeleteContactAccountJson?id={account.Id}",
                    confirm = $"Bu cari hesabi silmek istediginizden emin misiniz? ({account.AccountCode} — {account.AccountTitle})",
                },
            });
        }
        return entities;
    }

    // GET /Finance/ContactAccountEdit
    public async Task<IActionResult> ContactAccountEdit(int? id, CancellationToken cancellationToken)
    {
        ContactAccountDto? existing = null;
        if (id.HasValue && id.Value > 0)
        {
            existing = await _financeService.GetContactAccountByIdAsync(id.Value, cancellationToken);
        }

        ViewData["Title"] = existing is null ? "Yeni Cari Hesap" : $"Cari Düzenle — {existing.AccountCode}";
        ViewData["ContactAccount"] = existing;

        return View();
    }

    // GET /Finance/GetContactAccounts?accountType=1&search=abc
    [HttpGet]
    public async Task<IActionResult> GetContactAccounts(
        byte? accountType, string? search, CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await _financeService.GetContactAccountsAsync(accountType, search, cancellationToken);
            return Json(accounts);
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    // POST /Finance/UpsertContactAccount
    [HttpPost]
    public async Task<IActionResult> UpsertContactAccount(
        [FromBody] SaveContactAccountRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
            return Json(new { success = false, message = "Geçersiz istek verisi." });
        try
        {
            var (success, error, account) = await _financeService.UpsertContactAccountAsync(request, cancellationToken);
            if (!success)
                return Json(new { success = false, message = error });
            return Json(new { success = true, account });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatası: " + ex.Message });
        }
    }

    // POST /Finance/DeleteContactAccount
    [HttpPost]
    public async Task<IActionResult> DeleteContactAccount(
        [FromBody] DeleteContactAccountBody? body, CancellationToken cancellationToken)
    {
        if (body is null)
            return Json(new { success = false, message = "Geçersiz istek verisi." });
        try
        {
            var (success, error) = await _financeService.DeleteContactAccountAsync(body.Id, cancellationToken);
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
    public async Task<IActionResult> DeleteContactAccountJson(int id, CancellationToken ct)
    {
        try
        {
            var (success, error) = await _financeService.DeleteContactAccountAsync(id, ct);
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
