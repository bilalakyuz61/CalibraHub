using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Personel içe-aktarım handler'ı. Yazma <see cref="IPersonnelService.SaveAsync"/>'e
/// delege edilir — ad benzersizliği ve kod auto-türetme orada uygulanır
/// (kullanıcı kod girmez kuralı).
///
/// PIN ve kart numarası BİLİNÇLİ olarak içe aktarılamaz: kimlik doğrulama sırrıdır,
/// toplu dosya/DB üzerinden taşınmamalıdır. Güncellemede mevcut değerleri korunur.
/// </summary>
public sealed class PersonnelImportHandler : RowImportHandlerBase
{
    private readonly IPersonnelService _personnel;
    private List<PersonnelDto>? _cache;   // run-cache (scoped, satırlar sıralı işlenir)

    public PersonnelImportHandler(IPersonnelService personnel) => _personnel = personnel;

    public override string Entity => "PERSONNEL";
    public override string Label => "Personel";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new List<ImportTargetFieldDto>
    {
        new("FullName", "Ad Soyad", "string", true, true, "Personelin adı soyadı (zorunlu)", MaxLength: 200),
        new("Code", "Sicil No", "string", false, true, "Boşsa addan otomatik üretilir", MaxLength: 50),
        new("Title", "Görev", "string", false, false, MaxLength: 100),
        new("Department", "Departman", "string", false, false, MaxLength: 100),
        new("Phone", "Telefon", "string", false, false, MaxLength: 50),
        new("Email", "E-Posta", "string", false, false, MaxLength: 200),
        new("BirthDate", "Doğum Tarihi", "date", false, false),
        new("IsProductionOperator", "Üretim Operatörü", "bool", false, false, "Evet / Hayır",
            new[] { "Evet", "Hayır" }),
        new("IsActive", "Aktif", "bool", false, false, "Evet / Hayır", new[] { "Evet", "Hayır" }),
        new("Notes", "Notlar", "string", false, false),
    };

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "FullName"))) errs.Add("Ad soyad boş.");

        var birth = Get(d, "BirthDate");
        if (!string.IsNullOrWhiteSpace(birth) && ImportParse.ParseDate(birth) is null)
            errs.Add($"Doğum tarihi okunamadı: '{birth}'.");

        var email = Get(d, "Email");
        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@'))
            errs.Add($"E-posta geçersiz: '{email}'.");

        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys.Count == 0) return ("insert", null);

        var list = await EnsureAsync(ct);
        var hit = list.FirstOrDefault(p => KeysMatch(d, matchKeys, k => PersonnelValue(p, k)));
        return hit is not null ? ("update", hit.Id) : ("insert", null);
    }

    public override bool SupportsDeactivate => true;

    /// <summary>Kaynakta olmayan aktif personeli pasife alır (PIN/kart korunur).</summary>
    public override async Task<int> DeactivateAbsentAsync(ImportRowSet set, bool previewOnly, CancellationToken ct)
    {
        var sourceKeys = BuildSourceKeySet(set);
        if (sourceKeys.Count == 0) return 0;

        var all = await EnsureAsync(ct);
        var absent = all
            .Where(p => p.IsActive)
            .Where(p => KeySignature(k => PersonnelValue(p, k), set.MatchKeyFields) is string sig
                        && !sourceKeys.Contains(sig))
            .ToList();

        if (previewOnly) return absent.Count;

        foreach (var p in absent)
        {
            await _personnel.SaveAsync(new SavePersonnelRequest(
                Id: p.Id, Code: p.Code, FullName: p.FullName, Title: p.Title, Department: p.Department,
                PinCode: p.PinCode, CardNo: p.CardNo, IsProductionOperator: p.IsProductionOperator,
                IsActive: false, UserId: p.UserId, Phone: p.Phone, Email: p.Email,
                Notes: p.Notes, BirthDate: p.BirthDate), ct);
        }
        if (absent.Count > 0) _cache = null;
        return absent.Count;
    }

    /// <summary>Mevcut personelin hedef alan karşılığı (bileşik anahtar karşılaştırması için).</summary>
    private static string? PersonnelValue(PersonnelDto p, string key) => key.ToLowerInvariant() switch
    {
        "code"       => p.Code,
        "fullname"   => p.FullName,
        "email"      => p.Email,
        "phone"      => p.Phone,
        "department" => p.Department,
        _            => null,
    };

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        PersonnelDto? existing = null;
        if (action == "update" && existingId is > 0)
        {
            var list = await EnsureAsync(ct);
            existing = list.FirstOrDefault(p => p.Id == existingId.Value);
        }

        var req = new SavePersonnelRequest(
            Id: existing?.Id ?? 0,
            // Kod güncellemede korunur (eski referanslar bozulmasın); yeni kayıtta
            // boşsa servis addan türetir.
            Code: existing?.Code ?? (Get(d, "Code")?.Trim() ?? string.Empty),
            FullName: Pick(d, "FullName", existing?.FullName) ?? string.Empty,
            Title: Pick(d, "Title", existing?.Title),
            Department: Pick(d, "Department", existing?.Department),
            // PIN / kart içe aktarılmaz — mevcut değer korunur.
            PinCode: existing?.PinCode,
            CardNo: existing?.CardNo,
            IsProductionOperator: PickBool(d, "IsProductionOperator", existing?.IsProductionOperator ?? false),
            IsActive: PickBool(d, "IsActive", existing?.IsActive ?? true),
            UserId: existing?.UserId,
            Phone: Pick(d, "Phone", existing?.Phone),
            Email: Pick(d, "Email", existing?.Email),
            Notes: Pick(d, "Notes", existing?.Notes),
            BirthDate: PickDate(d, "BirthDate", existing?.BirthDate));

        var id = await _personnel.SaveAsync(req, ct);
        _cache = null;   // ad benzersizliği sonraki satırlarda güncel listeyi görmeli
        return (true, null, id);
    }

    private async Task<List<PersonnelDto>> EnsureAsync(CancellationToken ct)
        => _cache ??= (await _personnel.ListAsync(true, false, ct)).ToList();

    private static bool Same(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Eşlenmemiş (veya boş bırakılmış) alan mevcut değerini KORUR — aksi halde
    /// içe aktarım eşlenmeyen alanları sessizce siler.
    /// </summary>
    private static string? Pick(IReadOnlyDictionary<string, string?> d, string key, string? existing)
    {
        var v = Get(d, key);
        return string.IsNullOrWhiteSpace(v) ? existing : v.Trim();
    }

    private static bool PickBool(IReadOnlyDictionary<string, string?> d, string key, bool existing)
    {
        var v = Get(d, key);
        return string.IsNullOrWhiteSpace(v) ? existing : ParseBool(v);
    }

    private static DateTime? PickDate(IReadOnlyDictionary<string, string?> d, string key, DateTime? existing)
    {
        var v = Get(d, key);
        return string.IsNullOrWhiteSpace(v) ? existing : ImportParse.ParseDate(v);
    }
}
