using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Cari İletişim Kişisi (ContactPerson) içe-aktarım handler'ı. Her satır bir kişidir;
/// bağlı olduğu cari "Cari Kodu" ile çözülür (Contact.Id). Unvan adı verildiyse mevcut
/// unvan lookup'tan eşlenir (yoksa boş bırakılır). Yazma <see cref="IContactPersonRepository"/>.
/// </summary>
public sealed class ContactPersonImportHandler : RowImportHandlerBase
{
    private readonly IFinanceService _finance;
    private readonly IContactPersonRepository _personRepo;
    private readonly IContactPersonTitleRepository _titleRepo;

    public ContactPersonImportHandler(
        IFinanceService finance, IContactPersonRepository personRepo, IContactPersonTitleRepository titleRepo)
    { _finance = finance; _personRepo = personRepo; _titleRepo = titleRepo; }

    public override string Entity => "CONTACT_PERSON";
    public override string Label => "Cari İletişim Kişisi";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("ParentCode", "Cari Kodu",     "string", true,  false, "Kişinin bağlı olduğu carinin kodu (zorunlu)"),
        new ImportTargetFieldDto("FullName",   "Ad Soyad",      "string", true,  false, "İletişim kişisinin adı (zorunlu)"),
        new ImportTargetFieldDto("Title",      "Unvan",         "string", false, false, "Satış Müdürü, CFO... (mevcut unvanla eşleşir)"),
        new ImportTargetFieldDto("Phone",      "Telefon",       "string", false, false, null),
        new ImportTargetFieldDto("Email",      "E-posta",       "string", false, false, null),
        new ImportTargetFieldDto("Notes",      "Notlar",        "string", false, false, null),
        new ImportTargetFieldDto("IsPrimary",  "Birincil mi?",  "bool",   false, false, "Evet/Hayır — birincil iletişim kişisi"),
    };

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "ParentCode"))) errs.Add("Cari Kodu boş (kişi hangi cariye bağlı?).");
        if (string.IsNullOrWhiteSpace(Get(d, "FullName"))) errs.Add("Ad Soyad boş.");
        var email = Get(d, "Email");
        if (!string.IsNullOrWhiteSpace(email) && (!email.Contains('@') || !email.Contains('.'))) errs.Add("Geçersiz e-posta.");
        return errs;
    }

    // İletişim kişisi: her satır yeni kayıt (v1 — upsert yok).
    protected override Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, string? matchKeyField, CancellationToken ct)
        => Task.FromResult(("insert", (int?)null));

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var parentCode = Get(d, "ParentCode")!.Trim();
        var parent = await _finance.GetContactByCodeAsync(parentCode, ct);
        if (parent is null) return (false, $"Cari bulunamadı: '{parentCode}'", null);

        int? titleId = null;
        var title = Get(d, "Title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            var t = await _titleRepo.GetByNameAsync(title.Trim(), ct);
            titleId = t?.Id;   // bulunamazsa boş — v1'de otomatik unvan oluşturulmaz
        }

        var person = new ContactPerson
        {
            ContactId = parent.Id,
            TitleId   = titleId,
            FullName  = Get(d, "FullName")!.Trim(),
            Phone     = Get(d, "Phone"),
            Email     = Get(d, "Email"),
            Notes     = Get(d, "Notes"),
            IsPrimary = ParseBool(Get(d, "IsPrimary")),
            IsActive  = true,
            CreatedById = userId,
        };
        var id = await _personRepo.AddAsync(person, ct);
        return (true, null, id);
    }
}
