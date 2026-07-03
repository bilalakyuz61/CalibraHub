using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Cari (Contact) içe-aktarım handler'ı. Yazma <see cref="IFinanceService.UpsertContactAsync"/>'e
/// delege edilir; kod yoksa unvandan benzersiz türetilir; update'te eşlenmeyen alan korunur.
/// </summary>
public sealed class ContactImportHandler : RowImportHandlerBase
{
    private readonly IFinanceService _finance;
    private readonly IFinanceRepository _financeRepo;
    private readonly ISalesRepresentativeService _salesRep;
    private readonly ICariGroupService _cariGroup;
    private readonly IWidgetService _widget;
    private readonly IWidgetRepository _widgetRepo;
    private List<SalesRepresentativeDto>? _reps;   // run-cache (satış temsilcileri)
    private List<CariGroupDto>? _groups;           // run-cache (cari grupları)
    private FormDefinition? _form;                  // CONTACTS form (widget değer kaydı)
    private List<WidgetDefinition>? _widgets;       // Cari özel-alan (widget) tanımları

    public ContactImportHandler(IFinanceService finance, IFinanceRepository financeRepo, ISalesRepresentativeService salesRep, ICariGroupService cariGroup, IWidgetService widget, IWidgetRepository widgetRepo)
    { _finance = finance; _financeRepo = financeRepo; _salesRep = salesRep; _cariGroup = cariGroup; _widget = widget; _widgetRepo = widgetRepo; }

    public override string Entity => "CONTACT";
    public override string Label => "Cari Hesap";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields()
    {
        var fields = new List<ImportTargetFieldDto>
        {
        new ImportTargetFieldDto("AccountTitle",   "Cari Unvanı",   "string", true,  false, "Kurum veya kişi adı (zorunlu)"),
        new ImportTargetFieldDto("AccountCode",    "Cari Kodu",     "string", true,  true,  "Benzersiz cari kodu (zorunlu); eşleştirme anahtarı olabilir"),
        new ImportTargetFieldDto("AccountType",    "Cari Tipi",     "type",   false, false, "Müşteri / Satıcı / Her İkisi (boşsa Müşteri)", new[] { "Müşteri", "Satıcı", "Her İkisi" }),
        new ImportTargetFieldDto("TaxNumber",      "Vergi No",      "string", false, true,  "10 hane; eşleştirme anahtarı olabilir"),
        new ImportTargetFieldDto("TaxOffice",      "Vergi Dairesi", "string", false, false, null),
        new ImportTargetFieldDto("IdentityNumber", "TC Kimlik No",  "string", false, false, "11 hane"),
        new ImportTargetFieldDto("Phone",          "Telefon",       "string", false, false, null),
        new ImportTargetFieldDto("Mobile",         "Cep Telefonu",  "string", false, false, null),
        new ImportTargetFieldDto("Email",          "E-posta",       "string", false, false, null),
        new ImportTargetFieldDto("Website",        "Web Sitesi",    "string", false, false, null),
        new ImportTargetFieldDto("Address",        "Adres",         "string", false, false, null),
        new ImportTargetFieldDto("City",           "İl",            "string", false, false, null),
        new ImportTargetFieldDto("District",       "İlçe",          "string", false, false, null),
        new ImportTargetFieldDto("Neighborhood",   "Mahalle",       "string", false, false, null),
        new ImportTargetFieldDto("PostalCode",     "Posta Kodu",    "string", false, false, null),
        new ImportTargetFieldDto("CountryCode",    "Ülke Kodu",     "string", false, false, "TR, DE, US..."),
        new ImportTargetFieldDto("ContactPerson",  "İlgili Kişi",   "string", false, false, null),
        new ImportTargetFieldDto("SalesRepresentative", "Satış Temsilcisi", "string", false, false, "Mevcut satış temsilcisi adı (opsiyonel; verilirse eşleşmeli)"),
        new ImportTargetFieldDto("ContactGroup", "Cari Grubu", "string", false, false, "Cari grup KODU veya adı — Toptan/Perakende/VIP vb. (opsiyonel; verilirse eşleşmeli)"),
        };
        // Cari kartına admin'in eklediği özel (widget) alanlar — PreloadAsync ile yüklenir.
        if (_widgets is not null) fields.AddRange(_widgets.Select(WidgetToField));
        return fields;
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "AccountTitle"))) errs.Add("Cari unvanı boş.");
        if (string.IsNullOrWhiteSpace(Get(d, "AccountCode"))) errs.Add("Cari kodu boş.");
        var tax = DigitsOnly(Get(d, "TaxNumber") ?? "");
        if (tax.Length > 0 && tax.Length != 10) errs.Add($"Vergi no 10 hane olmalı (girilen: {tax.Length}).");
        var idn = DigitsOnly(Get(d, "IdentityNumber") ?? "");
        if (idn.Length > 0 && idn.Length != 11) errs.Add($"TC kimlik 11 hane olmalı (girilen: {idn.Length}).");
        var email = Get(d, "Email");
        if (!string.IsNullOrWhiteSpace(email) && (!email.Contains('@') || !email.Contains('.'))) errs.Add("Geçersiz e-posta.");
        // Satış temsilcisi verilmişse mevcut olmalı (_reps preview'da preload edilir; commit'te de doğrulanır).
        if (_reps is not null)
        {
            var rep = Get(d, "SalesRepresentative")?.Trim();
            if (!string.IsNullOrWhiteSpace(rep) && !_reps.Any(r => string.Equals(r.RepName?.Trim(), rep, StringComparison.OrdinalIgnoreCase)))
                errs.Add($"Satış temsilcisi bulunamadı: '{rep}'");
        }
        // Cari grubu verilmişse mevcut olmalı (kod VEYA ad ile).
        if (_groups is not null)
        {
            var grp = Get(d, "ContactGroup")?.Trim();
            if (!string.IsNullOrWhiteSpace(grp) && !_groups.Any(g =>
                    string.Equals(g.Code?.Trim(), grp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(g.Name?.Trim(), grp, StringComparison.OrdinalIgnoreCase)))
                errs.Add($"Cari grubu bulunamadı: '{grp}'");
        }
        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, string? matchKeyField, CancellationToken ct)
    {
        if (string.Equals(matchKeyField, "AccountCode", StringComparison.OrdinalIgnoreCase))
        {
            var code = Get(d, "AccountCode");
            if (!string.IsNullOrWhiteSpace(code))
            {
                var existing = await _financeRepo.GetContactByCodeAsync(code.Trim(), ct);
                if (existing is not null) return ("update", existing.Id);
            }
        }
        else if (string.Equals(matchKeyField, "TaxNumber", StringComparison.OrdinalIgnoreCase))
        {
            var tax = DigitsOnly(Get(d, "TaxNumber") ?? "");
            if (tax.Length == 10)
            {
                var existing = await _financeRepo.GetContactByTaxNumberAsync(tax, ct);
                if (existing is not null) return ("update", existing.Id);
            }
        }
        return ("insert", null);
    }

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        // Satış temsilcisi: verilmişse isimden Id'ye çöz (bulunamazsa satır hata); boşsa null/mevcut korunur.
        int? repId = null;
        var repName = Get(d, "SalesRepresentative")?.Trim();
        if (!string.IsNullOrWhiteSpace(repName))
        {
            repId = await ResolveSalesRepIdAsync(repName, ct);
            if (repId is null) return (false, $"Satış temsilcisi bulunamadı: '{repName}'", null);
        }
        // Cari grubu: verilmişse isimden Id'ye çöz (bulunamazsa satır hata).
        int? groupId = null;
        var groupName = Get(d, "ContactGroup")?.Trim();
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            groupId = await ResolveContactGroupIdAsync(groupName, ct);
            if (groupId is null) return (false, $"Cari grubu bulunamadı: '{groupName}'", null);
        }

        var request = action == "update" && existingId is > 0
            ? await BuildUpdateRequestAsync(d, existingId.Value, ct)
            : BuildInsertRequest(d, await DeriveUniqueCodeAsync(d, usedCodes, ct));
        if (repId is > 0) request = request with { SalesRepresentativeId = repId };
        if (groupId is > 0) request = request with { ContactGroupId = groupId };
        var (ok, err, dto) = await _finance.UpsertContactAsync(request, ct);
        if (!ok || dto is null) return (ok, err, dto?.Id);

        // Özel (widget) alan değerlerini WidgetTra'ya yaz (RecordId = AccountCode).
        if (_form is not null && _widgets is { Count: > 0 })
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in _widgets)
            {
                var v = Get(d, w.WidgetCode);
                if (!string.IsNullOrWhiteSpace(v)) values[w.WidgetCode] = v.Trim();
            }
            if (values.Count > 0)
            {
                // RecordId = Contact.Id (FinanceController batch render ile aynı konvansiyon; AccountCode DEĞİL).
                try { await _widget.SaveValuesAsync(new SaveWidgetValuesRequest(_form.Id, dto.Id.ToString(), values), ct); }
                catch (Exception ex) { return (false, $"Cari kaydedildi, özel alanlar kaydedilemedi: {ex.Message}", dto.Id); }
            }
        }
        return (ok, err, dto.Id);
    }

    private static SaveContactRequest BuildInsertRequest(IReadOnlyDictionary<string, string?> d, string code) => new(
        Id: null, AccountType: ResolveAccountType(Get(d, "AccountType")), AccountCode: code,
        AccountTitle: Get(d, "AccountTitle")!.Trim(),
        TaxNumber: Get(d, "TaxNumber"), IdentityNumber: Get(d, "IdentityNumber"), TaxOffice: Get(d, "TaxOffice"),
        Phone: Get(d, "Phone"), Email: Get(d, "Email"), Address: Get(d, "Address"), City: Get(d, "City"), District: Get(d, "District"),
        IsActive: true, PriceGroupId: null, CountryCode: Get(d, "CountryCode"), Mobile: Get(d, "Mobile"),
        Website: Get(d, "Website"), PostalCode: Get(d, "PostalCode"), ContactPerson: Get(d, "ContactPerson"), Neighborhood: Get(d, "Neighborhood"));

    private async Task<SaveContactRequest> BuildUpdateRequestAsync(IReadOnlyDictionary<string, string?> d, int existingId, CancellationToken ct)
    {
        var ex = await _finance.GetContactByIdAsync(existingId, ct) ?? throw new InvalidOperationException("Güncellenecek cari bulunamadı.");
        string? Ov(string key, string? cur) => d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : cur;
        byte at = d.TryGetValue("AccountType", out var t) && !string.IsNullOrWhiteSpace(t) ? ResolveAccountType(t) : ex.AccountType;
        return new SaveContactRequest(
            Id: ex.Id, AccountType: at, AccountCode: ex.AccountCode, AccountTitle: Ov("AccountTitle", ex.AccountTitle)!,
            TaxNumber: Ov("TaxNumber", ex.TaxNumber), IdentityNumber: Ov("IdentityNumber", ex.IdentityNumber), TaxOffice: Ov("TaxOffice", ex.TaxOffice),
            Phone: Ov("Phone", ex.Phone), Email: Ov("Email", ex.Email), Address: Ov("Address", ex.Address), City: Ov("City", ex.City), District: Ov("District", ex.District),
            IsActive: ex.IsActive, PriceGroupId: ex.PriceGroupId, CountryCode: Ov("CountryCode", ex.CountryCode), Mobile: Ov("Mobile", ex.Mobile),
            Website: Ov("Website", ex.Website), PostalCode: Ov("PostalCode", ex.PostalCode), ContactPerson: Ov("ContactPerson", ex.ContactPerson),
            Neighborhood: Ov("Neighborhood", ex.Neighborhood), SalesRepresentativeId: ex.SalesRepresentativeId, ContactGroupId: ex.ContactGroupId);
    }

    private async Task<string> DeriveUniqueCodeAsync(IReadOnlyDictionary<string, string?> d, HashSet<string> used, CancellationToken ct)
    {
        var mapped = Get(d, "AccountCode");
        if (!string.IsNullOrWhiteSpace(mapped))
        {
            var code = mapped.Trim().ToUpperInvariant();
            if (used.Add(code) && !await _financeRepo.CodeExistsAsync(code, null, ct)) return code;
        }
        var title = Get(d, "AccountTitle") ?? "CARI";
        var baseCode = new string(title.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (baseCode.Length == 0) baseCode = "CARI";
        if (baseCode.Length > 16) baseCode = baseCode[..16];
        var candidate = baseCode; int n = 1;
        while (used.Contains(candidate) || await _financeRepo.CodeExistsAsync(candidate, null, ct)) { n++; candidate = baseCode + n; }
        used.Add(candidate);
        return candidate;
    }

    private static byte ResolveAccountType(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v is "2" or "satıcı" or "satici" or "tedarikçi" or "tedarikci" or "supplier") return 2;
        if (v is "3" or "her ikisi" or "ikisi" or "both") return 3;
        return 1;
    }

    // Preview/Commit/katalog öncesi: satış temsilcisi + cari grup + özel-alan (widget) tanımlarını yükle.
    public override async Task PreloadAsync(CancellationToken ct)
    {
        _reps ??= (await _salesRep.GetAllAsync(ct)).ToList();
        _groups ??= (await _cariGroup.GetAllAsync(ct)).ToList();
        if (_widgets is null)
        {
            _form = await _widgetRepo.GetFormByCodeAsync(FormCodes.Contacts, ct);
            _widgets = _form is null
                ? new List<WidgetDefinition>()
                : (await _widgetRepo.GetWidgetsByFormAsync(_form.Id, ct))
                    .Where(w => !w.IsSystemField && w.IsActive && !IsContainerType(w.DataType))
                    .OrderBy(w => w.SortOrder).ToList();
        }
    }

    // Satış temsilcisi adı → Id (run-cache). Bulunamazsa null.
    private async Task<int?> ResolveSalesRepIdAsync(string name, CancellationToken ct)
    {
        _reps ??= (await _salesRep.GetAllAsync(ct)).ToList();
        var match = _reps.FirstOrDefault(r => string.Equals(r.RepName?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    // Cari grup kodu/adı → Id (run-cache). Önce kod, sonra ad. Bulunamazsa null.
    private async Task<int?> ResolveContactGroupIdAsync(string codeOrName, CancellationToken ct)
    {
        _groups ??= (await _cariGroup.GetAllAsync(ct)).ToList();
        var key = codeOrName.Trim();
        var match = _groups.FirstOrDefault(g => g.IsActive && string.Equals(g.Code?.Trim(), key, StringComparison.OrdinalIgnoreCase))
                 ?? _groups.FirstOrDefault(g => g.IsActive && string.Equals(g.Name?.Trim(), key, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    // Boş şablon + açılır liste için aktif satış temsilcisi + cari grup adları.
    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetDynamicAllowedValuesAsync(CancellationToken ct)
    {
        var reps = await _salesRep.GetAllAsync(ct);
        var groups = await _cariGroup.GetAllAsync(ct);
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SalesRepresentative"] = reps.Where(r => r.IsActive).Select(r => r.RepName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
            // Cari grup: KOD listesi (isim boş olabilir; kullanıcı kodla doldurur) — tamamı + dropdown.
            ["ContactGroup"]        = groups.Where(g => g.IsActive).Select(g => g.Code).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    // ── Özel alan (widget) yardımcıları ──────────────────────────────
    private static bool IsContainerType(string? dt) => dt is "group" or "grid" or "guide-list";

    private static ImportTargetFieldDto WidgetToField(WidgetDefinition w) =>
        new(w.WidgetCode, w.Label, MapWidgetType(w.DataType), w.IsRequired, false, $"Özel alan: {w.Label}", ParseOptions(w.OptionsJson));

    private static string MapWidgetType(string? dt) => dt switch
    {
        "numeric" => "decimal",
        "date" => "date",
        "boolean" => "bool",
        _ => "string",
    };

    // OptionsJson → açılır liste değerleri. ["A","B"] veya [{"l":"Label","k":"key"}] formatları.
    private static IReadOnlyList<string>? ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String) { var s = el.GetString(); if (!string.IsNullOrWhiteSpace(s)) list.Add(s!); }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    string? pick = null;
                    if (el.TryGetProperty("l", out var l) && l.ValueKind == JsonValueKind.String) pick = l.GetString();
                    else if (el.TryGetProperty("label", out var lab) && lab.ValueKind == JsonValueKind.String) pick = lab.GetString();
                    else if (el.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.String) pick = k.GetString();
                    if (!string.IsNullOrWhiteSpace(pick)) list.Add(pick!);
                }
            }
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }
}
