using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// 2026-06-20 — Şablon-tabanlı içe aktarım (AI'sız) orkestratörü.
/// Sorumluluk: şablon CRUD + Excel/CSV ayrıştırma + kolon→alan eşleme + boş şablon üretimi.
/// Entity-spesifik doğrulama/upsert <see cref="IImportTargetHandler"/> implementasyonlarına
/// delege edilir (Cari, Stok, Cari İletişim, ...). Yeni entity = yeni handler.
/// </summary>
public sealed class ImportService : IImportService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IImportTemplateRepository _templates;
    private readonly IExcelReader _excel;
    private readonly IReadOnlyDictionary<string, IImportTargetHandler> _handlers;
    private readonly IReadOnlyList<IImportTargetHandler> _handlerList;

    public ImportService(
        IImportTemplateRepository templates,
        IExcelReader excel,
        IEnumerable<IImportTargetHandler> handlers)
    {
        _templates = templates;
        _excel = excel;
        _handlerList = handlers.ToList();
        _handlers = _handlerList.ToDictionary(h => h.Entity.ToUpperInvariant(), h => h);
    }

    // ── Entity + alan kataloğu ───────────────────────────────────────────
    public IReadOnlyList<ImportEntityDto> GetEntities()
        => _handlerList.Select(h => new ImportEntityDto(h.Entity, h.Label)).ToList();

    public IReadOnlyList<ImportTargetFieldDto> GetTargetFields(string targetEntity)
        => ResolveHandler(targetEntity)?.GetFields() ?? Array.Empty<ImportTargetFieldDto>();

    public async Task<IReadOnlyList<ImportTargetFieldDto>> GetTargetFieldsAsync(string targetEntity, CancellationToken ct)
    {
        var handler = ResolveHandler(targetEntity);
        if (handler is null) return Array.Empty<ImportTargetFieldDto>();
        await handler.PreloadAsync(ct);   // widget/özel alanlar yüklenir → GetFields onları da içerir
        return handler.GetFields();
    }

    private IImportTargetHandler? ResolveHandler(string? entity)
    {
        var key = string.IsNullOrWhiteSpace(entity) ? "CONTACT" : entity.Trim().ToUpperInvariant();
        return _handlers.TryGetValue(key, out var h) ? h : null;
    }

    // ── Şablon CRUD ──────────────────────────────────────────────────────
    public async Task<IReadOnlyList<ImportTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken ct)
        => (await _templates.ListAsync(includeInactive, ct)).Select(ToDto).ToList();

    public async Task<ImportTemplateDto?> GetTemplateAsync(int id, CancellationToken ct)
    {
        var e = await _templates.GetByIdAsync(id, ct);
        return e is null ? null : ToDto(e);
    }

    public async Task<(bool Ok, string? Error, int Id)> SaveTemplateAsync(SaveImportTemplateRequest req, int? userId, CancellationToken ct)
    {
        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Şablon adı zorunludur.", 0);
        if (await _templates.NameExistsAsync(name, req.Id > 0 ? req.Id : null, ct))
            return (false, $"Aynı isimde başka bir şablon zaten tanımlı: '{name}'", 0);

        var columns = (req.Columns ?? Array.Empty<ImportColumnMapDto>()).Where(c => !string.IsNullOrWhiteSpace(c.TargetKey)).ToList();
        var entity = new ImportTemplate
        {
            Id = req.Id, Name = name,
            TargetEntity = string.IsNullOrWhiteSpace(req.TargetEntity) ? "CONTACT" : req.TargetEntity.Trim().ToUpperInvariant(),
            SheetName = string.IsNullOrWhiteSpace(req.SheetName) ? null : req.SheetName.Trim(),
            HeaderRowIndex = req.HeaderRowIndex < 1 ? 1 : req.HeaderRowIndex,
            MatchKeyField = string.IsNullOrWhiteSpace(req.MatchKeyField) ? null : req.MatchKeyField.Trim(),
            MappingJson = JsonSerializer.Serialize(columns, JsonOpts),
            IsActive = req.IsActive,
            CreatedById = userId is > 0 ? userId : null,
            UpdatedById = userId is > 0 ? userId : null,
        };
        var id = await _templates.SaveAsync(entity, ct);
        return (true, null, id);
    }

    public Task DeleteTemplateAsync(int id, CancellationToken ct) => _templates.DeleteAsync(id, ct);
    public Task<bool> ToggleTemplateAsync(int id, CancellationToken ct) => _templates.ToggleActiveAsync(id, ct);

    // ── Boş şablon (indirilebilir Excel) ─────────────────────────────────
    public async Task<(byte[] Bytes, string FileName)> BuildBlankTemplateAsync(string entity, int? templateId, CancellationToken ct)
    {
        var handler = ResolveHandler(entity);
        // dinamik (DB lookup) izinli değerler — örn. Cari İletişim "Unvan" mevcut unvan listesi
        var dynAllowed = handler is null
            ? (IReadOnlyDictionary<string, IReadOnlyList<string>>)new Dictionary<string, IReadOnlyList<string>>()
            : await handler.GetDynamicAllowedValuesAsync(ct);
        // enum → statik AllowedValues; dinamik alan → lookup; bool → otomatik Evet/Hayır.
        IReadOnlyList<string>? AllowedValuesFor(ImportTargetFieldDto f) =>
            f.AllowedValues
            ?? (dynAllowed.TryGetValue(f.Key, out var dvv) && dvv.Count > 0 ? dvv : null)
            ?? (string.Equals(f.DataType, "bool", StringComparison.OrdinalIgnoreCase) ? new[] { "Evet", "Hayır" } : null);

        if (handler is not null) await handler.PreloadAsync(ct);   // widget/özel alanları kataloğa kat
        var fields = handler?.GetFields() ?? Array.Empty<ImportTargetFieldDto>();
        var cols = new List<ExcelTemplateColumn>();
        var fileBase = handler?.Label ?? "Veri";

        if (templateId is > 0)
        {
            var t = await GetTemplateAsync(templateId.Value, ct);
            if (t is not null)
            {
                fileBase = t.Name;
                foreach (var f in fields)
                {
                    // TÜM alanları dahil et — şablonda eşlenmemiş (sonradan eklenen) alanlar da görünsün + değerleri gelsin.
                    var c = t.Columns.FirstOrDefault(x => string.Equals(x.TargetKey, f.Key, StringComparison.OrdinalIgnoreCase));
                    var header = c is not null && !string.IsNullOrWhiteSpace(c.SourceColumn) ? c.SourceColumn! : f.Label;
                    cols.Add(new ExcelTemplateColumn(header, f.Hint, f.IsRequired, AllowedValuesFor(f), f.CanBeMatchKey));
                }
            }
        }
        if (cols.Count == 0)
            cols = fields.Select(f => new ExcelTemplateColumn(f.Label, f.Hint, f.IsRequired, AllowedValuesFor(f), f.CanBeMatchKey)).ToList();

        // Anahtar (eşleştirme) alanları Excel'de ilk kolon(lar)a al — kullanıcı önce kodu girsin (stable sort).
        cols = cols.OrderByDescending(c => c.CanBeMatchKey).ToList();

        var bytes = _excel.WriteTemplate("Veri", cols);
        var safe = new string((fileBase ?? "Veri").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (string.IsNullOrEmpty(safe)) safe = "Veri";
        return (bytes, safe + "_Sablon.xlsx");
    }

    // ── Excel başlık okuma ───────────────────────────────────────────────
    public ImportHeaderReadDto ReadHeaders(byte[] data, string fileName, string? sheetName, int headerRowIndex)
    {
        try
        {
            var sheets = _excel.ListSheets(data, fileName).Select(s => new ImportSheetDto(s.Name, s.RowCount)).ToList();
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

    // ── Önizleme / Commit — handler'a delege ─────────────────────────────
    public async Task<ImportPreviewResultDto> PreviewAsync(SaveImportTemplateRequest spec, byte[] data, string fileName,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>? overrides, CancellationToken ct)
    {
        var handler = ResolveHandler(spec.TargetEntity);
        if (handler is null)
            return new ImportPreviewResultDto(false, $"Bilinmeyen hedef entity: {spec.TargetEntity}", 0, 0, 0, 0, 0,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ImportPreviewRowDto>());

        ExcelTable table;
        try { table = _excel.Read(data, fileName, spec.SheetName, spec.HeaderRowIndex < 1 ? 1 : spec.HeaderRowIndex); }
        catch (Exception ex)
        {
            return new ImportPreviewResultDto(false, $"Dosya okunamadı: {ex.Message}", 0, 0, 0, 0, 0,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ImportPreviewRowDto>());
        }
        return await handler.PreviewAsync(BuildRowSet(table, spec, overrides), ct);
    }

    public async Task<ImportCommitResultDto> CommitAsync(SaveImportTemplateRequest spec, byte[] data, string fileName, int? userId,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>? overrides, IReadOnlyCollection<int>? excluded, CancellationToken ct)
    {
        var handler = ResolveHandler(spec.TargetEntity);
        if (handler is null)
            return new ImportCommitResultDto(false, $"Bilinmeyen hedef entity: {spec.TargetEntity}", 0, 0, 0, Array.Empty<ImportCommitRowDto>());

        ExcelTable table;
        try { table = _excel.Read(data, fileName, spec.SheetName, spec.HeaderRowIndex < 1 ? 1 : spec.HeaderRowIndex); }
        catch (Exception ex)
        {
            return new ImportCommitResultDto(false, $"Dosya okunamadı: {ex.Message}", 0, 0, 0, Array.Empty<ImportCommitRowDto>());
        }
        return await handler.CommitAsync(BuildRowSet(table, spec, overrides, excluded), userId, ct);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────
    private static ImportRowSet BuildRowSet(ExcelTable table, SaveImportTemplateRequest spec,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>? overrides = null,
        IReadOnlyCollection<int>? excluded = null)
    {
        var headerIndex = BuildHeaderIndex(table.Headers);
        var columns = NormalizeColumns(spec.Columns);
        var rows = new List<IReadOnlyDictionary<string, string?>>(table.Rows.Count);
        int rowNo = 0;
        foreach (var r in table.Rows)
        {
            rowNo++;
            // Kullanıcının önizlemede iptal ettiği (hariç tuttuğu) satırı aktarmadan atla.
            if (excluded is not null && excluded.Contains(rowNo)) continue;
            var d = MapRow(r, headerIndex, columns);
            // Önizlemede elle düzeltilen hücreleri uygula (satır no, preview RowNumber ile birebir).
            if (overrides is not null && overrides.TryGetValue(rowNo, out var ov))
                foreach (var kv in ov)
                    d[kv.Key] = string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
            rows.Add(d);
        }
        var mappedKeys = columns.Select(c => c.TargetKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new ImportRowSet(rows, mappedKeys, spec.MatchKeyField);
    }

    private static List<ImportColumnMapDto> NormalizeColumns(IReadOnlyList<ImportColumnMapDto>? columns)
        => (columns ?? Array.Empty<ImportColumnMapDto>()).Where(c => !string.IsNullOrWhiteSpace(c.TargetKey)).ToList();

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

    private static Dictionary<string, string?> MapRow(IReadOnlyList<string> row, Dictionary<string, int> headerIndex, List<ImportColumnMapDto> columns)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            string? raw = null;
            if (!string.IsNullOrWhiteSpace(col.SourceColumn) && headerIndex.TryGetValue(col.SourceColumn.Trim(), out var idx) && idx < row.Count)
                raw = row[idx];
            var val = ApplyTransform(raw, col.Transform);
            if (string.IsNullOrEmpty(val)) val = string.IsNullOrWhiteSpace(col.DefaultValue) ? null : col.DefaultValue.Trim();
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
            "upper" => v.ToUpperInvariant(),
            "lower" => v.ToLowerInvariant(),
            "digits" => new string(v.Where(char.IsDigit).ToArray()),
            _ => v,
        };
    }

    private static ImportTemplateDto ToDto(ImportTemplate e)
    {
        List<ImportColumnMapDto> columns;
        try { columns = JsonSerializer.Deserialize<List<ImportColumnMapDto>>(e.MappingJson, JsonOpts) ?? new(); }
        catch { columns = new(); }
        return new ImportTemplateDto(e.Id, e.Name, e.TargetEntity, e.SheetName, e.HeaderRowIndex, e.MatchKeyField, columns, e.IsActive, e.Created, e.Updated);
    }
}
