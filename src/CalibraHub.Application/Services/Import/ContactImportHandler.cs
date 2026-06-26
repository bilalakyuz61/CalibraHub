using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Cari (Contact) içe-aktarım handler'ı. Yazma <see cref="IFinanceService.UpsertContactAsync"/>'e
/// delege edilir; kod yoksa unvandan benzersiz türetilir; update'te eşlenmeyen alan korunur.
/// </summary>
public sealed class ContactImportHandler : RowImportHandlerBase
{
    private readonly IFinanceService _finance;
    private readonly IFinanceRepository _financeRepo;

    public ContactImportHandler(IFinanceService finance, IFinanceRepository financeRepo)
    { _finance = finance; _financeRepo = financeRepo; }

    public override string Entity => "CONTACT";
    public override string Label => "Cari Hesap";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("AccountTitle",   "Cari Unvanı",   "string", true,  false, "Kurum veya kişi adı (zorunlu)"),
        new ImportTargetFieldDto("AccountCode",    "Cari Kodu",     "string", true,  true,  "Benzersiz cari kodu (zorunlu); eşleştirme anahtarı olabilir"),
        new ImportTargetFieldDto("AccountType",    "Cari Tipi",     "type",   false, false, "Müşteri / Satıcı / Her İkisi (boşsa Müşteri)"),
        new ImportTargetFieldDto("TaxNumber",      "Vergi No",      "string", false, false, "10 hane"),
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
    };

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
        return ("insert", null);
    }

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var request = action == "update" && existingId is > 0
            ? await BuildUpdateRequestAsync(d, existingId.Value, ct)
            : BuildInsertRequest(d, await DeriveUniqueCodeAsync(d, usedCodes, ct));
        var (ok, err, dto) = await _finance.UpsertContactAsync(request, ct);
        return (ok, err, dto?.Id);
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
}
