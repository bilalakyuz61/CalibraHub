using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class FinanceController : Controller
{
    private readonly IFinanceService _financeService;
    private readonly IWidgetService _widgetService;
    private readonly IDocumentService _documentService;
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly ISalesRepresentativeService _salesRepService;

    private const int DefaultPageSize = 50;

    public FinanceController(
        IFinanceService financeService,
        IWidgetService widgetService,
        IDocumentService documentService,
        IDocumentTypeRepository docTypeRepo,
        ISalesRepresentativeService salesRepService)
    {
        _financeService = financeService;
        _widgetService = widgetService;
        _documentService = documentService;
        _docTypeRepo = docTypeRepo;
        _salesRepService = salesRepService;
    }

    // GET /Finance/GetContactQuotes?contactId=X — cariye ait verilen teklifler
    [HttpGet]
    public async Task<IActionResult> GetContactQuotes(int contactId, CancellationToken ct)
    {
        if (contactId <= 0) return Json(System.Array.Empty<object>());
        var quotes = await _documentService.GetQuotesByContactAsync(contactId, ct);
        return Json(quotes.Select(q => new
        {
            id = q.Id,
            documentNumber = q.DocumentNumber,
            documentDate = q.DocumentDate,
            validUntil = q.ValidUntil,
            currencyId = q.CurrencyId,
            currency = q.CurrencyCode,    // display-only (geri uyum: 'currency' key korunur)
            currencySymbol = q.CurrencySymbol,
            grandTotal = q.GrandTotal,
            status = q.Status,
            lineCount = q.LineCount
        }));
    }

    /// <summary>
    /// GET /Finance/GetContact?id=X veya /Finance/GetContact?code=ABC
    /// Stok karti GetMaterialCard pattern'inin cari versiyonu — ContactEdit ve
    /// DocumentEdit'in cari kod input'lari kullanir. ContactEdit ekranlarinda
    /// in-place AJAX fill icin TUM form alanlari donulur (sayfa reload yok).
    /// Bulunamazsa 404 → istemci tarafi /Finance/ContactEdit?new=1&code=X (yeni cari).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetContact(int id, string? code, CancellationToken ct)
    {
        ContactDto? contact = id > 0
            ? await _financeService.GetContactByIdAsync(id, ct)
            : await _financeService.GetContactByCodeAsync(code ?? string.Empty, ct);
        if (contact is null) return NotFound();

        // Tum form alanlari (ContactEdit AJAX fill icin) — Razor view'daki @existing?.X esleri ile birebir.
        return Json(new
        {
            id              = contact.Id,
            accountCode     = contact.AccountCode,
            accountTitle    = contact.AccountTitle,
            accountType     = contact.AccountType,
            taxNumber       = contact.TaxNumber,
            identityNumber  = contact.IdentityNumber,
            taxOffice       = contact.TaxOffice,
            contactPerson   = contact.ContactPerson,
            phone           = contact.Phone,
            mobile          = contact.Mobile,
            waPhone         = contact.WaPhone,
            waName          = contact.WaName,
            email           = contact.Email,
            website         = contact.Website,
            address         = contact.Address,
            city            = contact.City,
            district        = contact.District,
            neighborhood    = contact.Neighborhood,
            postalCode      = contact.PostalCode,
            countryCode     = contact.CountryCode,
            isActive        = contact.IsActive,
            priceGroupId    = contact.PriceGroupId,
            salesRepId      = contact.SalesRepresentativeId,
            contactGroupId  = contact.ContactGroupId,
        });
    }

    // GET /Finance/GetContactMovements?contactId=X&documentTypeId=&fromDate=&toDate= — cariye ait hareketler
    [HttpGet]
    public async Task<IActionResult> GetContactMovements(int contactId, int? documentTypeId, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        if (contactId <= 0) return Json(System.Array.Empty<object>());
        var movements = await _documentService.GetMovementsByContactAsync(contactId, documentTypeId, fromDate, toDate, ct);
        var docTypes = await _docTypeRepo.GetAllAsync(ct);
        var typeMap = docTypes.ToDictionary(t => t.Id, t => new { t.Code, t.Name });
        return Json(movements.Select(d => new
        {
            id = d.Id,
            documentNumber = d.DocumentNumber,
            documentDate = d.DocumentDate,
            validUntil = d.ValidUntil,
            currencyId = d.CurrencyId,
            currency = d.CurrencyCode,    // display-only
            currencySymbol = d.CurrencySymbol,
            grandTotal = d.GrandTotal,
            status = d.Status,
            lineCount = d.LineCount,
            documentTypeId = d.DocumentTypeId,
            documentTypeCode = d.DocumentTypeId.HasValue && typeMap.TryGetValue(d.DocumentTypeId.Value, out var t1) ? t1.Code : null,
            documentTypeName = d.DocumentTypeId.HasValue && typeMap.TryGetValue(d.DocumentTypeId.Value, out var t2) ? t2.Name : null
        }));
    }

    // GET /Finance/GetDocumentTypes — aktif belge tipleri (filtre dropdown icin)
    [HttpGet]
    public async Task<IActionResult> GetDocumentTypes(CancellationToken ct)
    {
        var types = await _docTypeRepo.GetAllAsync(ct);
        return Json(types.Where(t => t.IsActive).Select(t => new { id = t.Id, code = t.Code, name = t.Name }));
    }

    // GET /Finance/GetSalesRepsList — satis temsilcileri dropdown icin
    [HttpGet]
    public async Task<IActionResult> GetSalesRepsList(CancellationToken ct)
    {
        var reps = await _salesRepService.GetAllAsync(ct);
        return Json(reps.Where(r => r.IsActive).Select(r => new { id = r.Id, name = r.RepName }));
    }

    // GET /Finance/Contacts — anlik render (DB cagirisi YOK). HTML tarayiciya
    // <100ms icinde dusuyor, spinner hemen gorunur. Tum config + ilk sayfa
    // verisi tek bir combined AJAX (`GetContactsInitialPayload`) ile yuklenir.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Contacts)]
    public IActionResult Contacts()
    {
        return View(new ContactsViewModel { BoardConfig = null });
    }

    // GET /Finance/GetContactsInitialPayload — tek atista config + ilk sayfa.
    // Frontend bu cagri ile mount yapar; SmartBoard initialEntities ile gelir,
    // useEffect[searchQuery]'in mount ek fetch tetiklemesini engellemek icin
    // skipInitialFetch=true bayragi config'e gomulur.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Contacts)]
    public async Task<IActionResult> GetContactsInitialPayload(CancellationToken ct)
    {
        var (accounts, totalCount) = await _financeService.GetContactsPagedAsync(
            null, null, 0, DefaultPageSize, ct);
        var masterWidgets = await BuildMasterWidgetsAsync(ct);
        var entities = await BuildEntitiesAsync(accounts, ct);

        return Json(new
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
            pageSize = DefaultPageSize,
            skipInitialFetch = true,   // SmartBoard mount'ta ek fetchPage(1) atmasin
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
        });
    }

    // GET /Finance/GetContactsBoardConfig — eski endpoint, deprecated; yeni
    // initial payload akisina gectik. Yine de refresh icin korunuyor (sadece
    // config doner, entities bos).
    [HttpGet]
    public async Task<IActionResult> GetContactsBoardConfig(CancellationToken ct)
    {
        // Sadece count icin minimal sorgu — pageSize=1 ile satir okuma maliyeti
        // ihmal edilebilir; COUNT(*) OVER() totalCount'i tek sorguda doner.
        var (_, totalCount) = await _financeService.GetContactsPagedAsync(null, null, 0, 1, ct);
        var masterWidgets = await BuildMasterWidgetsAsync(ct);

        return Json(new
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
            pageSize = DefaultPageSize,
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
            entities = Array.Empty<object>(),  // SmartBoard fetchPage(1)'i kendi cagirir
        });
    }

    // GET /Finance/GetContactsPage?page=1&pageSize=50&search=abc
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Contacts)]
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
            return Json(new { error = "Islem sirasinda bir hata olustu." });
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
        var contactsSchema = await _widgetService.GetFormSchemaByCodeAsync("CONTACTS", ct);
        var masterWidgets = SmartBoardFilterHelpers.BuildAdminFormWidgets(contactsSchema);
        var typeOptions = SmartBoardFilterHelpers.ToOptionsList(new[] { "Musteri", "Tedarikci" });
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("sys_phone",    "Telefon",  "phone"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("sys_email",    "E-Posta",  "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("sys_tax_no",   "Vergi No", "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("sys_city",     "İl",       "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("sys_district", "İlçe",     "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeOptionsWidget("sys_type",     "Tip",      typeOptions));
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
                widgets.Add(new { id = "sys_phone",  type = "data", dataType = "phone", label = "Telefon",  value = account.Phone,      color = "blue"    });
            if (!string.IsNullOrWhiteSpace(account.Email))
                widgets.Add(new { id = "sys_email",  type = "data", dataType = "text", label = "E-Posta",  value = account.Email,      color = "cyan"    });
            if (!string.IsNullOrWhiteSpace(account.TaxNumber))
                widgets.Add(new { id = "sys_tax_no", type = "data", dataType = "text", label = "Vergi No", value = account.TaxNumber,  color = "slate"   });
            if (!string.IsNullOrWhiteSpace(account.City))
                widgets.Add(new { id = "sys_city",   type = "data", dataType = "text", label = "İl",       value = account.City,       color = "teal"    });
            if (!string.IsNullOrWhiteSpace(account.District))
                widgets.Add(new { id = "sys_district", type = "data", dataType = "text", label = "İlçe",   value = account.District,   color = "teal"    });
            widgets.Add(new { id = "sys_type", type = "data", dataType = "options", label = "Tip",
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
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ContactEdit)]
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
            return Json(new { error = "Islem sirasinda bir hata olustu." });
        }
    }

    // POST /Finance/UpsertContact
    [HttpPost]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ContactEdit)]
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
            return Json(new { success = false, message = "Sunucu hatası: " + "Islem sirasinda bir hata olustu." });
        }
    }

    // POST /Finance/DeleteContact
    [HttpPost]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ContactEdit)]
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
            return Json(new { success = false, message = "Sunucu hatası: " + "Islem sirasinda bir hata olustu." });
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
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }
}
