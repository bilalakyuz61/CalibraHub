using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// 2026-06-20 — Şablon-tabanlı içe aktarım (AI'sız). Cari pilotu.
///   Şablon CRUD + Excel/CSV okuma + satır eşleme/doğrulama + commit.
/// Yazma, <see cref="IFinanceService.UpsertContactAsync"/>'e delege edilir; böylece
/// kod-uniqueness, tip kontrolü ve domain validasyonu birebir korunur.
/// </summary>
public sealed class ImportService : IImportService
{
    private const int PreviewDetailLimit = 500;   // önizlemede dönen detaylı satır sınırı
    private const int CommitRowLimit = 10_000;     // tek commit'te işlenen max satır

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IImportTemplateRepository _templates;
    private readonly IExcelReader _excel;
    private readonly IFinanceService _finance;
    private readonly IFinanceRepository _financeRepo;

    public ImportService(
        IImportTemplateRepository templates,
        IExcelReader excel,
        IFinanceService finance,
        IFinanceRepository financeRepo)
    {
        _templates = templates;
        _excel = excel;
        _finance = finance;
        _financeRepo = financeRepo;
    }

    // ── Hedef alan kataloğu (Cari pilot) ─────────────────────────────────
    public IReadOnlyList<ImportTargetFieldDto> GetTargetFields(string targetEntity)
    {
        // Pilot kapsamı yalnızca CONTACT; bilinmeyen entity için boş döner.
        if (!string.Equals(targetEntity, "CONTACT", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<ImportTargetFieldDto>();

        return new[]
        {
            new ImportTargetFieldDto("AccountTitle",   "Cari Unvanı",   "string", true,  false, "Kurum veya kişi adı (zorunlu)"),
            new ImportTargetFieldDto("AccountCode",    "Cari Kodu",     "string", false, true,  "Boşsa otomatik üretilir; eşleştirme anahtarı olabilir"),
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
    }

    // ── Şablon CRUD ──────────────────────────────────────────────────────
    public async Task<IReadOnlyList<ImportTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken ct)
    {
        var list = await _templates.ListAsync(includeInactive, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<ImportTemplateDto?> GetTemplateAsync(int id, CancellationToken ct)
    {
        var e = await _templates.GetByIdAsync(id, ct);
        return e is null ? null : ToDto(e);
    }

    public async Task<(bool Ok, string? Error, int Id)> SaveTemplateAsync(SaveImportTemplateRequest req, int? userId, CancellationToken ct)
    {
        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Şablon adı zorunludur.", 0);

        // Kullanıcı kod girmez kuralı → benzersizlik ad üzerinden.
        if (await _templates.NameExistsAsync(name, req.Id > 0 ? req.Id : null, ct))
            return (false, $"Aynı isimde başka bir şablon zaten tanımlı: '{name}'", 0);

        var columns = (req.Columns ?? Array.Empty<ImportColumnMapDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.TargetKey))
            .ToList();

        var entity = new ImportTemplate
        {
            Id             = req.Id,
            Name           = name,
            TargetEntity   = string.IsNullOrWhiteSpace(req.TargetEntity) ? "CONTACT" : req.TargetEntity.Trim().ToUpperInvariant(),
            SheetName      = string.IsNullOrWhiteSpace(req.SheetName) ? null : req.SheetName.Trim(),
            HeaderRowIndex = req.HeaderRowIndex < 1 ? 1 : req.HeaderRowIndex,
            MatchKeyField  = string.IsNullOrWhiteSpace(req.MatchKeyField) ? null : req.MatchKeyField.Trim(),
            MappingJson    = JsonSerializer.Serialize(columns, JsonOpts),
            IsActive       = req.IsActive,
            CreatedById    = userId is > 0 ? userId : null,
            UpdatedById    = userId is > 0 ? userId : null,
        };

        var id = await _templates.SaveAsync(entity, ct);
        return (true, null, id);
    }

    public Task DeleteTemplateAsync(int id, CancellationToken ct) => _templates.DeleteAsync(id, ct);
    public Task<bool> ToggleTemplateAsync(int id, CancellationToken ct) => _templates.ToggleActiveAsync(id, ct);

    // ── Boş şablon (indirilebilir Excel) ─────────────────────────────────
    public async Task<(byte[] Bytes, string FileName)> BuildBlankTemplateAsync(string entity, int? templateId, CancellationToken ct)
    {
        var fields = GetTargetFields(string.IsNullOrWhiteSpace(entity) ? "CONTACT" : entity);
        var cols = new List<ExcelTemplateColumn>();
        var fileBase = "Cari";

        // Kayıtlı şablon seçiliyse: o şablonun kaynak kolon adlarını başlık yap (birebir round-trip).
        if (templateId is > 0)
        {
            var t = await GetTemplateAsync(templateId.Value, ct);
            if (t is not null)
            {
                fileBase = t.Name;
                foreach (var f in fields)   // katalog sırasını koru
                {
                    var c = t.Columns.FirstOrDefault(x => string.Equals(x.TargetKey, f.Key, StringComparison.OrdinalIgnoreCase));
                    if (c is not null && !string.IsNullOrWhiteSpace(c.SourceColumn))
                        cols.Add(new ExcelTemplateColumn(c.SourceColumn!, f.Hint, f.IsRequired));
                }
            }
        }

        // Şablon yok / boş → tüm hedef alan etiketleri (auto-map ile birebir eşleşir).
        if (cols.Count == 0)
            cols = fields.Select(f => new ExcelTemplateColumn(f.Label, f.Hint, f.IsRequired)).ToList();

        var bytes = _excel.WriteTemplate("Cari", cols);
        var safe = new string((fileBase ?? "Cari").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (string.IsNullOrEmpty(safe)) safe = "Cari";
        return (bytes, safe + "_Sablon.xlsx");
    }

    // ── Excel başlık okuma ───────────────────────────────────────────────
    public ImportHeaderReadDto ReadHeaders(byte[] data, string fileName, string? sheetName, int headerRowIndex)
    {
        try
        {
            var sheets = _excel.ListSheets(data, fileName)
                .Select(s => new ImportSheetDto(s.Name, s.RowCount)).ToList();

            var table = _excel.Read(data, fileName, sheetName, headerRowIndex < 1 ? 1 : headerRowIndex);
            var sample = table.Rows.Take(5).Select(r => (IReadOnlyList<string>)r.ToList()).ToList();

            return new ImportHeaderReadDto(true, null, sheets, table.SheetName, table.Headers, sample);
        }
        catch (Exception ex)
        {
            return new ImportHeaderReadDto(false, $"Dosya okunamadı: {ex.Message}",
                Array.Empty<ImportSheetDto>(), null, Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());
        }
    }

    // ── Önizleme ─────────────────────────────────────────────────────────
    public async Task<ImportPreviewResultDto> PreviewAsync(SaveImportTemplateRequest spec, byte[] data, string fileName, CancellationToken ct)
    {
        ExcelTable table;
        try { table = _excel.Read(data, fileName, spec.SheetName, spec.HeaderRowIndex < 1 ? 1 : spec.HeaderRowIndex); }
        catch (Exception ex)
        {
            return new ImportPreviewResultDto(false, $"Dosya okunamadı: {ex.Message}", 0, 0, 0, 0, 0,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ImportPreviewRowDto>());
        }

        var headerIndex = BuildHeaderIndex(table.Headers);
        var columns = NormalizeColumns(spec.Columns);
        var (colKeys, colLabels) = DisplayColumns(columns);

        int total = 0, valid = 0, error = 0, ins = 0, upd = 0;
        var detail = new List<ImportPreviewRowDto>();

        foreach (var row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();
            total++;
            var mapped = MapRow(row, headerIndex, columns);
            var errors = ValidateRow(mapped);

            string action;
            if (errors.Count > 0) { action = "error"; error++; }
            else
            {
                var (act, _) = await ResolveActionAsync(mapped, spec, ct);
                action = act;
                valid++;
                if (act == "update") upd++; else ins++;
            }

            if (detail.Count < PreviewDetailLimit)
            {
                var cells = colKeys.Select(k => new ImportPreviewCellDto(k, mapped.GetValueOrDefault(k))).ToList();
                detail.Add(new ImportPreviewRowDto(total, action, cells, errors));
            }
        }

        return new ImportPreviewResultDto(true, null, total, valid, error, ins, upd, colKeys, colLabels, detail);
    }

    // ── Commit ───────────────────────────────────────────────────────────
    public async Task<ImportCommitResultDto> CommitAsync(SaveImportTemplateRequest spec, byte[] data, string fileName, int? userId, CancellationToken ct)
    {
        ExcelTable table;
        try { table = _excel.Read(data, fileName, spec.SheetName, spec.HeaderRowIndex < 1 ? 1 : spec.HeaderRowIndex); }
        catch (Exception ex)
        {
            return new ImportCommitResultDto(false, $"Dosya okunamadı: {ex.Message}", 0, 0, 0, Array.Empty<ImportCommitRowDto>());
        }

        var headerIndex = BuildHeaderIndex(table.Headers);
        var columns = NormalizeColumns(spec.Columns);

        int inserted = 0, updated = 0, failed = 0, rowNo = 0;
        var results = new List<ImportCommitRowDto>();
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;
            if (rowNo > CommitRowLimit)
            {
                results.Add(new ImportCommitRowDto(rowNo, false, "error", $"Satır sınırı aşıldı (max {CommitRowLimit}).", null));
                failed++;
                continue;
            }

            var mapped = MapRow(row, headerIndex, columns);
            var errors = ValidateRow(mapped);
            if (errors.Count > 0)
            {
                results.Add(new ImportCommitRowDto(rowNo, false, "error", string.Join("; ", errors), null));
                failed++;
                continue;
            }

            try
            {
                var (action, existingId) = await ResolveActionAsync(mapped, spec, ct);
                SaveContactRequest request = action == "update" && existingId is > 0
                    ? await BuildUpdateRequestAsync(mapped, existingId.Value, ct)
                    : BuildInsertRequest(mapped, await DeriveUniqueCodeAsync(mapped, usedCodes, ct));

                var (ok, err, dto) = await _finance.UpsertContactAsync(request, ct);
                if (ok)
                {
                    if (action == "update") updated++; else inserted++;
                    results.Add(new ImportCommitRowDto(rowNo, true, action, null, dto?.Id));
                }
                else
                {
                    failed++;
                    results.Add(new ImportCommitRowDto(rowNo, false, action, err ?? "Bilinmeyen hata", null));
                }
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new ImportCommitRowDto(rowNo, false, "error", ex.Message, null));
            }
        }

        return new ImportCommitResultDto(true, null, inserted, updated, failed, results);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────

    private static List<ImportColumnMapDto> NormalizeColumns(IReadOnlyList<ImportColumnMapDto>? columns) =>
        (columns ?? Array.Empty<ImportColumnMapDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.TargetKey))
            .ToList();

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> headers)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            var key = (headers[i] ?? string.Empty).Trim();
            if (key.Length > 0 && !d.ContainsKey(key)) d[key] = i;
        }
        return d;
    }

    private (IReadOnlyList<string> Keys, IReadOnlyList<string> Labels) DisplayColumns(List<ImportColumnMapDto> columns)
    {
        var catalog = GetTargetFields("CONTACT").ToDictionary(f => f.Key, f => f.Label, StringComparer.OrdinalIgnoreCase);
        var keys = new List<string>();
        var labels = new List<string>();
        // Katalog sırasını koru
        foreach (var f in GetTargetFields("CONTACT"))
        {
            if (columns.Any(c => string.Equals(c.TargetKey, f.Key, StringComparison.OrdinalIgnoreCase)))
            {
                keys.Add(f.Key);
                labels.Add(f.Label);
            }
        }
        return (keys, labels);
    }

    /// <summary>Bir veri satırını şablona göre hedef alan değerlerine eşle.</summary>
    private static Dictionary<string, string?> MapRow(
        IReadOnlyList<string> row, Dictionary<string, int> headerIndex, List<ImportColumnMapDto> columns)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            string? raw = null;
            if (!string.IsNullOrWhiteSpace(col.SourceColumn)
                && headerIndex.TryGetValue(col.SourceColumn.Trim(), out var idx)
                && idx < row.Count)
                raw = row[idx];

            var val = ApplyTransform(raw, col.Transform);
            if (string.IsNullOrEmpty(val))
                val = string.IsNullOrWhiteSpace(col.DefaultValue) ? null : col.DefaultValue.Trim();

            d[col.TargetKey] = val;
        }
        return d;
    }

    private static string? ApplyTransform(string? raw, string? transform)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return (transform ?? "").ToLowerInvariant() switch
        {
            "upper"  => v.ToUpperInvariant(),
            "lower"  => v.ToLowerInvariant(),
            "digits" => DigitsOnly(v),
            _        => v,
        };
    }

    private static List<string> ValidateRow(Dictionary<string, string?> d)
    {
        var errs = new List<string>();

        if (string.IsNullOrWhiteSpace(Get(d, "AccountTitle")))
            errs.Add("Cari unvanı boş.");

        var tax = DigitsOnly(Get(d, "TaxNumber") ?? "");
        if (tax.Length > 0 && tax.Length != 10)
            errs.Add($"Vergi no 10 hane olmalı (girilen: {tax.Length}).");

        var idn = DigitsOnly(Get(d, "IdentityNumber") ?? "");
        if (idn.Length > 0 && idn.Length != 11)
            errs.Add($"TC kimlik 11 hane olmalı (girilen: {idn.Length}).");

        var email = Get(d, "Email");
        if (!string.IsNullOrWhiteSpace(email) && (!email.Contains('@') || !email.Contains('.')))
            errs.Add("Geçersiz e-posta.");

        return errs;
    }

    /// <summary>Upsert anahtarına göre insert/update kararı + mevcut kayıt Id'si.</summary>
    private async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        Dictionary<string, string?> d, SaveImportTemplateRequest spec, CancellationToken ct)
    {
        if (string.Equals(spec.MatchKeyField, "AccountCode", StringComparison.OrdinalIgnoreCase))
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

    private SaveContactRequest BuildInsertRequest(Dictionary<string, string?> d, string code) => new(
        Id:             null,
        AccountType:    ResolveAccountType(Get(d, "AccountType")),
        AccountCode:    code,
        AccountTitle:   Get(d, "AccountTitle")!.Trim(),
        TaxNumber:      Get(d, "TaxNumber"),
        IdentityNumber: Get(d, "IdentityNumber"),
        TaxOffice:      Get(d, "TaxOffice"),
        Phone:          Get(d, "Phone"),
        Email:          Get(d, "Email"),
        Address:        Get(d, "Address"),
        City:           Get(d, "City"),
        District:       Get(d, "District"),
        IsActive:       true,
        PriceGroupId:   null,
        CountryCode:    Get(d, "CountryCode"),
        Mobile:         Get(d, "Mobile"),
        Website:        Get(d, "Website"),
        PostalCode:     Get(d, "PostalCode"),
        ContactPerson:  Get(d, "ContactPerson"),
        Neighborhood:   Get(d, "Neighborhood"));

    /// <summary>
    /// Update'te eşlenmeyen alanlar mevcut değerini korur — boş hücre var olan veriyi EZMEZ.
    /// AccountCode değişmez (eski referanslar bozulmasın).
    /// </summary>
    private async Task<SaveContactRequest> BuildUpdateRequestAsync(Dictionary<string, string?> d, int existingId, CancellationToken ct)
    {
        var ex = await _finance.GetContactByIdAsync(existingId, ct)
                 ?? throw new InvalidOperationException("Güncellenecek cari bulunamadı.");

        // Sadece eşlenmiş + dolu değer üzerine yazılır.
        string? Ov(string key, string? current) =>
            d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : current;

        byte accountType = d.TryGetValue("AccountType", out var at) && !string.IsNullOrWhiteSpace(at)
            ? ResolveAccountType(at)
            : ex.AccountType;

        return new SaveContactRequest(
            Id:             ex.Id,
            AccountType:    accountType,
            AccountCode:    ex.AccountCode,                          // değişmez
            AccountTitle:   Ov("AccountTitle", ex.AccountTitle)!,
            TaxNumber:      Ov("TaxNumber", ex.TaxNumber),
            IdentityNumber: Ov("IdentityNumber", ex.IdentityNumber),
            TaxOffice:      Ov("TaxOffice", ex.TaxOffice),
            Phone:          Ov("Phone", ex.Phone),
            Email:          Ov("Email", ex.Email),
            Address:        Ov("Address", ex.Address),
            City:           Ov("City", ex.City),
            District:       Ov("District", ex.District),
            IsActive:       ex.IsActive,
            PriceGroupId:   ex.PriceGroupId,
            CountryCode:    Ov("CountryCode", ex.CountryCode),
            Mobile:         Ov("Mobile", ex.Mobile),
            Website:        Ov("Website", ex.Website),
            PostalCode:     Ov("PostalCode", ex.PostalCode),
            ContactPerson:  Ov("ContactPerson", ex.ContactPerson),
            Neighborhood:   Ov("Neighborhood", ex.Neighborhood),
            SalesRepresentativeId: ex.SalesRepresentativeId,
            ContactGroupId: ex.ContactGroupId);
    }

    /// <summary>
    /// Cari kodu eşlenmemişse unvandan benzersiz kod türet (kullanıcı kod girmez kuralı).
    /// Eşlenmişse onu kullan. Aynı dosya içinde + DB'de benzersizliği garantiler.
    /// </summary>
    private async Task<string> DeriveUniqueCodeAsync(Dictionary<string, string?> d, HashSet<string> used, CancellationToken ct)
    {
        var mapped = Get(d, "AccountCode");
        if (!string.IsNullOrWhiteSpace(mapped))
        {
            var code = mapped.Trim().ToUpperInvariant();
            if (used.Add(code) && !await _financeRepo.CodeExistsAsync(code, null, ct))
                return code;
            // Çakışma → ekli kod türetmeye düş (aşağıdaki base mantığı)
        }

        var title = Get(d, "AccountTitle") ?? "CARI";
        var baseCode = new string(title.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (baseCode.Length == 0) baseCode = "CARI";
        if (baseCode.Length > 16) baseCode = baseCode[..16];

        var candidate = baseCode;
        int n = 1;
        while (used.Contains(candidate) || await _financeRepo.CodeExistsAsync(candidate, null, ct))
        {
            n++;
            candidate = baseCode + n;
        }
        used.Add(candidate);
        return candidate;
    }

    private static byte ResolveAccountType(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v is "2" or "satıcı" or "satici" or "tedarikçi" or "tedarikci" or "supplier" or "satici")
            return 2;
        if (v is "3" or "her ikisi" or "ikisi" or "both")
            return 3;
        return 1; // 1 / müşteri / customer / boş → Müşteri
    }

    private static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    private static string? Get(Dictionary<string, string?> d, string key) =>
        d.TryGetValue(key, out var v) ? v : null;

    private static ImportTemplateDto ToDto(ImportTemplate e)
    {
        List<ImportColumnMapDto> columns;
        try
        {
            columns = JsonSerializer.Deserialize<List<ImportColumnMapDto>>(e.MappingJson, JsonOpts)
                      ?? new List<ImportColumnMapDto>();
        }
        catch { columns = new List<ImportColumnMapDto>(); }

        return new ImportTemplateDto(
            e.Id, e.Name, e.TargetEntity, e.SheetName, e.HeaderRowIndex, e.MatchKeyField,
            columns, e.IsActive, e.Created, e.Updated);
    }
}
