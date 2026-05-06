using System.Globalization;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// EAV widget sisteminin tercuman katmani.
///
/// Sorumluluklar:
///   - Schema yukleme (form + widget tanimlari)
///   - DataType'a gore value serialize / deserialize
///   - Validation (numeric/date/boolean parse, dropdown value kontrolu, multi-select array)
///   - Render DTO'larini hazirlama (schema + value birlesimi)
///
/// "Her Sey Metindir" kuralini burada uygulariz: DB tarafina her zaman string
/// yazilir, okunurken DataType'a gore dogru tipe parse edilir.
/// </summary>
public sealed class WidgetService : IWidgetService
{
    private readonly IWidgetRepository _repository;

    public WidgetService(IWidgetRepository repository)
    {
        _repository = repository;
    }

    // ══════════════════════════════════════════════════════════
    // Schema
    // ══════════════════════════════════════════════════════════

    public async Task<WidgetFormSchemaDto?> GetFormSchemaAsync(int formId, CancellationToken ct)
    {
        var form = await _repository.GetFormByIdAsync(formId, ct);
        if (form == null) return null;
        return await BuildSchemaAsync(form, ct);
    }

    public async Task<WidgetFormSchemaDto?> GetFormSchemaByCodeAsync(string formCode, CancellationToken ct)
    {
        var form = await _repository.GetFormByCodeAsync(formCode, ct);
        if (form == null) return null;
        return await BuildSchemaAsync(form, ct);
    }

    private async Task<WidgetFormSchemaDto> BuildSchemaAsync(FormDefinition form, CancellationToken ct)
    {
        // includeInactive=true: admin panel pasif widget'lari da gormeli (toggle ile aktif/pasif yapabilsin)
        var widgets = await _repository.GetWidgetsByFormAsync(form.Id, ct, includeInactive: true);
        var dtos = widgets
            .Select(w => new WidgetDefinitionDto(
                w.Id,
                w.ParentId,
                w.WidgetCode,
                w.Label,
                w.DataType,
                w.MaxLength,
                w.SortOrder,
                ParseOptions(w.OptionsJson),
                w.IsActive,
                ParseMetadata(w.OptionsJson),
                ParseRules(w.RulesJson),
                w.IsPlainField,
                w.IsRequired,
                MinLength: w.MinLength,
                ExpectedLength: w.ExpectedLength,
                MinValue: w.MinValue,
                MaxValue: w.MaxValue,
                ColorType: w.ColorType,
                ColorValue: w.ColorValue,
                ColSpan: w.ColSpan,
                LabelStyle: w.LabelStyle ?? "standard"))
            .ToArray();

        return new WidgetFormSchemaDto(
            FormId: form.Id,
            FormCode: form.FormCode,
            FormLabel: form.FormName,
            Widgets: dtos);
    }

    // ══════════════════════════════════════════════════════════
    // Render
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<WidgetRenderDto>> GetRenderModelAsync(
        int formId,
        string recordId,
        CancellationToken ct)
    {
        var widgets = await _repository.GetWidgetsByFormAsync(formId, ct);
        var values = await _repository.GetValuesAsync(formId, recordId, ct);
        var baseDtos = BuildRenderDtos(widgets, values);
        return await EnrichGridsAsync(baseDtos, recordId, ct);
    }

    public async Task<WidgetRecordDto?> GetRecordByCodeAsync(
        string formCode,
        string recordId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode)) return null;

        var form = await _repository.GetFormByCodeAsync(formCode, ct);
        if (form == null) return null;

        var widgets = await _repository.GetWidgetsByFormAsync(form.Id, ct);
        // recordId bos ise (yeni kayit) values bos liste — widgets aktif olanlar gosterilir
        var values = string.IsNullOrWhiteSpace(recordId)
            ? Array.Empty<Domain.Entities.WidgetValue>()
            : (await _repository.GetValuesAsync(form.Id, recordId, ct)).ToArray();

        var baseDtos = BuildRenderDtos(widgets, values);
        var enriched = await EnrichGridsAsync(baseDtos, recordId, ct);

        return new WidgetRecordDto(
            FormId: form.Id,
            FormCode: form.FormCode,
            FormLabel: form.FormName,
            RecordId: recordId ?? string.Empty,
            Widgets: enriched);
    }

    /// <summary>
    /// Faz E — grid widget'lar icin post-pass: her grid widget'inin metadata'sindan
    /// childFormCode'u okur, bu child form'daki parentRecordId=masterRecordId olan
    /// tum satirlari toplar, her satirin degerlerini widgetCode->value dict'ine
    /// cevirir ve GridRowDto listesi olarak ilgili WidgetRenderDto'ya iseler.
    /// Master recordId bos ise (yeni kayit) grid'ler bos liste doner.
    /// </summary>
    private async Task<IReadOnlyCollection<WidgetRenderDto>> EnrichGridsAsync(
        IReadOnlyCollection<WidgetRenderDto> baseDtos,
        string masterRecordId,
        CancellationToken ct)
    {
        // Grid widget yoksa hicbir sey yapma — hizli yol
        var gridDtos = baseDtos
            .Where(d => string.Equals(d.DataType, "grid", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (gridDtos.Length == 0) return baseDtos;

        // Master recordId bos ise grid satir okunmaz — yeni kayit senaryosu
        if (string.IsNullOrWhiteSpace(masterRecordId))
        {
            return baseDtos
                .Select(d => string.Equals(d.DataType, "grid", StringComparison.OrdinalIgnoreCase)
                    ? d with { GridRows = Array.Empty<GridRowDto>() }
                    : d)
                .ToArray();
        }

        // Her grid icin child satirlari topla
        var gridRowsByWidgetId = new Dictionary<int, IReadOnlyCollection<GridRowDto>>();
        foreach (var g in gridDtos)
        {
            var childFormCode = g.Metadata != null && g.Metadata.TryGetValue("childFormCode", out var c) ? c : null;
            if (string.IsNullOrWhiteSpace(childFormCode))
            {
                gridRowsByWidgetId[g.Id] = Array.Empty<GridRowDto>();
                continue;
            }

            var childForm = await _repository.GetFormByCodeAsync(childFormCode, ct);
            if (childForm == null)
            {
                gridRowsByWidgetId[g.Id] = Array.Empty<GridRowDto>();
                continue;
            }

            var childRecordIds = await _repository.GetChildRecordIdsAsync(childForm.Id, masterRecordId, ct);
            if (childRecordIds.Count == 0)
            {
                gridRowsByWidgetId[g.Id] = Array.Empty<GridRowDto>();
                continue;
            }

            // Child form'un aktif widget tanimlarini (kod → id mapping icin) cek
            var childWidgets = await _repository.GetWidgetsByFormAsync(childForm.Id, ct);
            var childWidgetById = childWidgets.ToDictionary(w => w.Id, w => w);

            var rows = new List<GridRowDto>(childRecordIds.Count);
            foreach (var rid in childRecordIds)
            {
                var childValues = await _repository.GetValuesAsync(childForm.Id, rid, ct);
                var valuesDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in childValues)
                {
                    if (!childWidgetById.TryGetValue(v.WidgetId, out var cw)) continue;
                    if (!cw.IsActive) continue;
                    if (string.Equals(cw.DataType, "group", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(cw.DataType, "grid",  StringComparison.OrdinalIgnoreCase)) continue;
                    valuesDict[cw.WidgetCode] = ParseValueForRender(v.Value, cw.DataType);
                }
                rows.Add(new GridRowDto(RecordId: rid, Values: valuesDict));
            }
            // RecordId alfanumerik sirala (deterministik gorunum)
            rows.Sort((a, b) => string.Compare(a.RecordId, b.RecordId, StringComparison.Ordinal));
            gridRowsByWidgetId[g.Id] = rows;
        }

        // Sonuc listesini tekrar uret — grid widget'lara GridRows yerlestir
        return baseDtos
            .Select(d =>
            {
                if (!string.Equals(d.DataType, "grid", StringComparison.OrdinalIgnoreCase)) return d;
                return d with { GridRows = gridRowsByWidgetId.TryGetValue(d.Id, out var rows) ? rows : Array.Empty<GridRowDto>() };
            })
            .ToArray();
    }

    /// <summary>
    /// widgets + values → WidgetRenderDto list. Ortak helper, hem
    /// GetRenderModelAsync hem GetRecordByCodeAsync kullanir.
    /// Sadece aktif widget'lar donulur; grup satirlari da dahil.
    /// </summary>
    private static IReadOnlyCollection<WidgetRenderDto> BuildRenderDtos(
        IReadOnlyCollection<Domain.Entities.WidgetDefinition> widgets,
        IReadOnlyCollection<Domain.Entities.WidgetValue> values)
    {
        var valueByWidgetId = values.ToDictionary(v => v.WidgetId, v => v.Value);
        var result = new List<WidgetRenderDto>();
        foreach (var w in widgets.Where(x => x.IsActive).OrderBy(x => x.SortOrder))
        {
            valueByWidgetId.TryGetValue(w.Id, out var rawValue);
            var parsed = ParseValueForRender(rawValue, w.DataType);

            result.Add(new WidgetRenderDto(
                Id: w.Id,
                ParentId: w.ParentId,
                SortOrder: w.SortOrder,
                WidgetId: w.WidgetCode,
                Label: w.Label,
                DataType: w.DataType,
                Options: ParseOptions(w.OptionsJson),
                Value: parsed,
                Metadata: ParseMetadata(w.OptionsJson),
                Rules: ParseRules(w.RulesJson),
                IsPlainField: w.IsPlainField,
                IsRequired: w.IsRequired,
                MaxLength: w.MaxLength,
                MinLength: w.MinLength,
                ExpectedLength: w.ExpectedLength,
                MinValue: w.MinValue,
                MaxValue: w.MaxValue,
                ColorType: w.ColorType,
                ColorValue: w.ColorValue,
                ColSpan: w.ColSpan,
                LabelStyle: w.LabelStyle ?? "standard"));
        }
        return result;
    }

    // ══════════════════════════════════════════════════════════
    // Save (validation + serialization)
    // ══════════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════════
    // Admin CRUD (Faz B)
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<FormCatalogItemDto>> GetFormsAsync(CancellationToken ct)
    {
        var forms = await _repository.GetFormsAsync(ct);
        return forms
            .Select(f => new FormCatalogItemDto(
                f.Id, f.FormCode, f.FormName, f.Module, f.SubModule, f.SortOrder))
            .ToArray();
    }

    public async Task<int> UpsertWidgetAsync(UpsertWidgetRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.FormId <= 0) throw new ArgumentException("FormId gecersiz.");
        if (string.IsNullOrWhiteSpace(request.WidgetCode)) throw new ArgumentException("WidgetCode bos olamaz.");
        if (string.IsNullOrWhiteSpace(request.Label)) throw new ArgumentException("Label bos olamaz.");
        if (string.IsNullOrWhiteSpace(request.DataType)) throw new ArgumentException("DataType bos olamaz.");
        if (request.ColSpan.HasValue && (request.ColSpan.Value < 1 || request.ColSpan.Value > 24))
            throw new ArgumentException($"ColSpan 1-24 arasinda olmali, alinan: {request.ColSpan.Value}");

        // Form var mi?
        var form = await _repository.GetFormByIdAsync(request.FormId, ct);
        if (form == null) throw new ArgumentException($"FormId={request.FormId} bulunamadi.");

        // WidgetCode normalize — lowercase, harfle basla, a-z0-9_
        var widgetCode = NormalizeWidgetCode(request.WidgetCode);

        // DataType normalize (lowercase + whitelist)
        var dataType = (request.DataType ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidDataType(dataType))
        {
            throw new ArgumentException($"Gecersiz DataType '{request.DataType}'. Izinli: text, numeric, date, boolean, dropdown, multi-select, group, link, lookup, grid, guide-list.");
        }

        // ParentId kontrolu — varsa gercekten grup olmali
        if (request.ParentId.HasValue)
        {
            var parent = await _repository.GetWidgetByIdAsync(request.ParentId.Value, ct);
            if (parent == null) throw new ArgumentException($"ParentId={request.ParentId} bulunamadi.");
            if (!string.Equals(parent.DataType, "group", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Parent widget bir grup olmali (DataType='group').");
            if (parent.FormId != request.FormId)
                throw new ArgumentException("Parent widget farkli bir forma ait, gecisli gruplama yasak.");
        }

        // Grup ise ParentId daima null, Options + MaxLength yok
        var isGroup = dataType == "group";
        int? parentId = isGroup ? null : request.ParentId;
        string? optionsJson = null;
        if (!isGroup && (dataType == "dropdown" || dataType == "multi-select"))
        {
            // Options mecburi degil (henuz bos kayit olabilir) ama varsa JSON'a cevir
            if (request.Options != null && request.Options.Count > 0)
            {
                var cleaned = request.Options
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => o.Trim())
                    .ToArray();
                optionsJson = JsonSerializer.Serialize(cleaned);
            }
        }
        else if (dataType == "link")
        {
            // Link tipi icin OptionsJSON zorunlu — tek elemanli string dizi.
            // OptionsJSON[0] = "/Hedef/Sayfa?param={value}" URL sablonu.
            // {value} runtime'da kullanicinin girdigi deger ile yer degistirir.
            var tpl = request.Options?
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tpl))
            {
                throw new ArgumentException("Baglanti (link) tipi icin hedef URL sablonu zorunludur. Orn: /Finance/ContactEdit?code={value}");
            }
            optionsJson = JsonSerializer.Serialize(new[] { tpl });
        }
        else if (dataType == "lookup" || dataType == "guide-list")
        {
            // Lookup ve Guide-List tipi icin OptionsJSON object shape:
            //   { "guideCode": "...", "guideConfig": "{...}" }
            // React request.Options:
            //   [0] = guideCode (zorunlu — her iki tipte de rehber olmadan widget anlamsiz)
            //   [1] = guideConfig JSON string (opsiyonel — gorunur kolonlar/etiketler/SQL kisiti)
            // Guide-List salt okunur akordion liste olsa da rehber tanimi metadata
            // ayni formatta tutulur — Lookup ile ortak parsing.
            var nf = request.Options?
                .Select(o => o ?? string.Empty)
                .ToList() ?? new List<string>();
            var guideCode = nf.ElementAtOrDefault(0)?.Trim();
            if (string.IsNullOrWhiteSpace(guideCode))
            {
                var humanLabel = dataType == "lookup" ? "Rehber (lookup)" : "Rehber Listesi";
                throw new ArgumentException($"{humanLabel} tipi icin guideCode zorunludur.");
            }
            var obj = new Dictionary<string, string> { ["guideCode"] = guideCode };
            // guideConfig opsiyonel; varsa JSON string olarak ekle (backend yorumlamaz, runtime React kullanir).
            var cfg = nf.ElementAtOrDefault(1);
            if (!string.IsNullOrWhiteSpace(cfg)) obj["guideConfig"] = cfg.Trim();
            // displayScope (sadece guide-list) — 'form' | 'card' | 'both'. Lookup'ta da
            // gelirse problem yok; runtime tarafi dataType-bazli kontrol eder.
            var scope = nf.ElementAtOrDefault(2)?.Trim().ToLowerInvariant();
            if (scope == "form" || scope == "card" || scope == "both")
                obj["displayScope"] = scope;
            optionsJson = JsonSerializer.Serialize(obj);
        }
        else if (dataType == "grid")
        {
            // Grid tipi (master-detail) icin OptionsJSON object shape:
            // {"childFormCode":"SALES_QUOTE_LINES"}
            // React request.Options[0] olarak childFormCode gonderir.
            var childFormCode = request.Options?
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(childFormCode))
            {
                throw new ArgumentException("Alt tablo (grid) tipi icin childFormCode zorunludur.");
            }
            // Child form gercekten var mi? Dogrulama.
            var childForm = await _repository.GetFormByCodeAsync(childFormCode, ct);
            if (childForm == null)
            {
                throw new ArgumentException($"Child form bulunamadi: '{childFormCode}'");
            }
            var obj = new Dictionary<string, string> { ["childFormCode"] = childFormCode };
            optionsJson = JsonSerializer.Serialize(obj);
        }
        else if (dataType == "numeric")
        {
            // Numeric format metadata: numericFormat, decimalSep, thousandSep
            // React request.Options[0..2] olarak gonderir.
            var nf = request.Options?
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .ToArray() ?? [];
            if (nf.Length > 0)
            {
                var obj = new Dictionary<string, string>
                {
                    ["numericFormat"] = nf.ElementAtOrDefault(0) ?? "number",
                    ["decimalSep"] = nf.ElementAtOrDefault(1) ?? ",",
                    ["thousandSep"] = nf.ElementAtOrDefault(2) ?? ".",
                    ["decimalPlaces"] = nf.ElementAtOrDefault(3) ?? "2"
                };
                optionsJson = JsonSerializer.Serialize(obj);
            }
        }
        else if (dataType == "text")
        {
            // Text + opsiyonel rehber. Options[0]=guideCode, Options[1] iki sekilde gelebilir:
            //   (a) Yeni format — guideConfig JSON object: '{"viewCode":"...","columns":[...],"constraint":"..."}'
            //   (b) Legacy format — constraints array (token destekli WHERE): '[{...}, ...]' veya plain string
            // JSON object ise guideConfig, degilse constraints olarak saklanir.
            var textOpts = request.Options?
                .Select(o => o?.Trim() ?? "")
                .ToArray() ?? [];
            var guideCode = textOpts.ElementAtOrDefault(0) ?? "";
            var secondRaw = textOpts.ElementAtOrDefault(1) ?? "";
            if (!string.IsNullOrWhiteSpace(guideCode))
            {
                var obj = new Dictionary<string, object>
                {
                    ["guideCode"] = guideCode,
                };
                if (!string.IsNullOrWhiteSpace(secondRaw))
                {
                    var trimmed = secondRaw.TrimStart();
                    // Object → guideConfig (yeni). Array veya plain → constraints (legacy).
                    if (trimmed.StartsWith("{"))
                    {
                        obj["guideConfig"] = secondRaw;
                    }
                    else
                    {
                        obj["constraints"] = secondRaw;
                    }
                }
                optionsJson = JsonSerializer.Serialize(obj);
            }
        }
        int? maxLength      = isGroup ? null : request.MaxLength;
        int? minLength      = isGroup ? null : request.MinLength;
        int? expectedLength = isGroup ? null : request.ExpectedLength;
        decimal? minValue   = isGroup ? null : request.MinValue;
        decimal? maxValue   = isGroup ? null : request.MaxValue;

        // Faz G — Rules validation + serialize
        // Kurallar saglanmissa her slot'i regex whitelist'ten ve yasakli kelime
        // filtresinden gecirip obje olarak saklariz. Gruplarda / grid'de kural yok.
        string? rulesJson = null;
        if (!isGroup && request.Rules != null)
        {
            var rulesClean = SanitizeRules(request.Rules);
            if (rulesClean != null)
            {
                rulesJson = JsonSerializer.Serialize(rulesClean);
            }
        }

        // Duplicate WidgetCode check (ayni form icinde)
        var existing = await _repository.GetWidgetsByFormAsync(request.FormId, ct);
        var duplicate = existing.FirstOrDefault(w =>
            string.Equals(w.WidgetCode, widgetCode, StringComparison.OrdinalIgnoreCase) &&
            (!request.Id.HasValue || w.Id != request.Id.Value));
        if (duplicate != null)
        {
            throw new ArgumentException($"'{widgetCode}' WidgetCode bu formda zaten mevcut.");
        }

        // LabelStyle whitelist: 'standard' (varsayilan) / 'modern' / 'inline'.
        // Eski IsPlainField=true gelirse 'inline'a kopru — yeni clientlar artik
        // sadece LabelStyle gonderir, eski clientlar ile geriye-donuk uyumlu.
        var rawLabelStyle = (request.LabelStyle ?? string.Empty).Trim().ToLowerInvariant();
        string normalizedLabelStyle;
        if (rawLabelStyle == "modern") normalizedLabelStyle = "modern";
        else if (rawLabelStyle == "inline") normalizedLabelStyle = "inline";
        else if (request.IsPlainField) normalizedLabelStyle = "inline";  // legacy back-compat
        else normalizedLabelStyle = "standard";

        var now = DateTime.Now;
        var widget = new WidgetDefinition
        {
            Id = request.Id ?? 0,
            FormId = request.FormId,
            ParentId = parentId,
            WidgetCode = widgetCode,
            Label = request.Label.Trim(),
            DataType = dataType,
            MaxLength      = maxLength,
            MinLength      = minLength,
            ExpectedLength = expectedLength,
            MinValue       = minValue,
            MaxValue       = maxValue,
            SortOrder = request.SortOrder,
            OptionsJson = optionsJson,
            RulesJson = rulesJson,
            // Eski DB okuyuculari icin senkronize: LabelStyle='inline' ↔ IsPlainField=true.
            IsPlainField = normalizedLabelStyle == "inline",
            IsRequired = request.IsRequired,
            IsActive = request.IsActive,
            ColorType  = request.ColorType,
            ColorValue = string.IsNullOrWhiteSpace(request.ColorValue) ? null : request.ColorValue.Trim(),
            // ColSpan — 1-24 arasi clamp; null/sifir gelirse varsayilan 12 (1/2 satir).
            ColSpan = (request.ColSpan is int cs && cs >= 1 && cs <= 24) ? cs : 12,
            LabelStyle = normalizedLabelStyle,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var newId = await _repository.UpsertWidgetAsync(widget, ct);

        // Faz H: Flattened View regenerate — try/catch, hata widget save'i etkilemez
        await RegenerateFlattenedViewSafeAsync(request.FormId, ct);

        return newId;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<WidgetRenderDto>>> GetBatchRenderModelsAsync(
        string formCode,
        IReadOnlyCollection<string> recordIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(formCode) || recordIds.Count == 0) return result;

        var form = await _repository.GetFormByCodeAsync(formCode, ct);
        if (form == null) return result;

        var allWidgets = await _repository.GetWidgetsByFormAsync(form.Id, ct);
        // SmartCard icin sadece duz alanlar — grup ve grid cikar
        var displayWidgets = allWidgets
            .Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase))
            // guide-list (salt okunur akordion liste) SmartCard'da da gosterilir;
            // kullanici karttan dogrudan ozet liste gorebilsin diye filter dahili.
            .ToArray();

        if (displayWidgets.Length == 0) return result;

        var valuesByRecord = await _repository.GetValuesBatchAsync(form.Id, recordIds, ct);

        foreach (var rid in recordIds)
        {
            var values = valuesByRecord[rid].ToArray();
            result[rid] = BuildRenderDtos(displayWidgets, values);
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<(string WidgetCode, string Label)>>>
        ValidateRequiredAsync(string formCode, IReadOnlyCollection<string> recordIds, CancellationToken ct)
    {
        var empty = new Dictionary<string, IReadOnlyList<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(formCode) || recordIds.Count == 0) return empty;

        var form = await _repository.GetFormByCodeAsync(formCode, ct);
        if (form == null) return empty;

        var widgets = await _repository.GetWidgetsByFormAsync(form.Id, ct);
        var required = widgets
            .Where(w => w.IsActive && w.IsRequired
                && !string.Equals(w.DataType, "group",      StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",       StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "guide-list", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (required.Length == 0) return empty;

        var valuesByRecord = await _repository.GetValuesBatchAsync(form.Id, recordIds, ct);
        var result = new Dictionary<string, IReadOnlyList<(string, string)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rid in recordIds)
        {
            // valuesByRecord ILookup — bulunmayan anahtar bos enumerable doner.
            var valuesByWidgetId = valuesByRecord[rid].ToDictionary(v => v.WidgetId, v => v.Value);
            var missing = new List<(string, string)>();
            foreach (var w in required)
            {
                valuesByWidgetId.TryGetValue(w.Id, out var raw);
                if (string.IsNullOrWhiteSpace(raw))
                    missing.Add((w.WidgetCode, w.Label ?? w.WidgetCode));
            }
            if (missing.Count > 0) result[rid] = missing;
        }

        return result;
    }

    public async Task ToggleIsPlainFieldAsync(int widgetId, bool isPlainField, CancellationToken ct)
    {
        var widget = await _repository.GetWidgetByIdAsync(widgetId, ct)
            ?? throw new KeyNotFoundException($"Widget {widgetId} bulunamadi.");

        var updated = new Domain.Entities.WidgetDefinition
        {
            Id             = widget.Id,
            FormId         = widget.FormId,
            ParentId       = widget.ParentId,
            WidgetCode     = widget.WidgetCode,
            Label          = widget.Label,
            DataType       = widget.DataType,
            MaxLength      = widget.MaxLength,
            MinLength      = widget.MinLength,
            ExpectedLength = widget.ExpectedLength,
            MinValue       = widget.MinValue,
            MaxValue       = widget.MaxValue,
            SortOrder      = widget.SortOrder,
            OptionsJson    = widget.OptionsJson,
            RulesJson      = widget.RulesJson,
            IsPlainField   = isPlainField,
            IsRequired     = widget.IsRequired,
            IsActive       = widget.IsActive,
            ColorType      = widget.ColorType,
            ColorValue     = widget.ColorValue,
            ColSpan        = widget.ColSpan,
            LabelStyle     = widget.LabelStyle ?? "standard",
            CreatedAt      = widget.CreatedAt,
            UpdatedAt      = DateTime.Now,
        };
        await _repository.UpsertWidgetAsync(updated, ct);
    }

    public async Task DeleteWidgetAsync(int widgetId, CancellationToken ct)
    {
        if (widgetId <= 0) throw new ArgumentException("Widget Id gecersiz.");

        var widget = await _repository.GetWidgetByIdAsync(widgetId, ct)
            ?? throw new ArgumentException("Widget bulunamadi.");

        // Grup silme guard'i — cocuklu grup silinemez (orphan ParentId engellenir).
        if (string.Equals(widget.DataType, "group", StringComparison.OrdinalIgnoreCase))
        {
            var childCount = await _repository.CountChildrenByParentIdAsync(widgetId, ct);
            if (childCount > 0)
            {
                throw new ArgumentException(
                    $"Bu grubun icinde {childCount} widget var. Once icindeki widget'lari silin veya baska gruba tasiyin.");
            }
        }

        var formIdForRegen = widget.FormId;
        await _repository.DeleteWidgetAsync(widgetId, ct);

        // Faz H: Flattened View regenerate
        await RegenerateFlattenedViewSafeAsync(formIdForRegen, ct);
    }

    /// <summary>
    /// Faz H — Widget upsert/delete sonrasi v_Flat_{FormCode} view'ini yeniden
    /// olusturur. Hata olursa sessizce loglar, widget save akisini etkilemez.
    /// View yalnizca raporlama kolayligidir — kritik path degil.
    /// </summary>
    private async Task RegenerateFlattenedViewSafeAsync(int formId, CancellationToken ct)
    {
        try
        {
            var form = await _repository.GetFormByIdAsync(formId, ct);
            if (form == null) return;
            if (string.IsNullOrWhiteSpace(form.BaseTable) || string.IsNullOrWhiteSpace(form.BaseRecordKey))
                return;  // Base table yoksa view uretilmez
            var widgets = await _repository.GetWidgetsByFormAsync(formId, ct);
            await _repository.RegenerateFlattenedViewAsync(form, widgets, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FlatView] Regen failed for formId={formId}: {ex.Message}");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex WidgetCodeRegex =
        new(@"^[a-z][a-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string NormalizeWidgetCode(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        // Turkce karakterleri normalize et
        s = s.Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u')
             .Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        // Bosluklari _ yap
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "_");
        // Gecersiz karakterleri kaldir
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9_]", "");
        if (!WidgetCodeRegex.IsMatch(s))
            throw new ArgumentException("WidgetCode kucuk harf, rakam, alt cizgi icermeli; harfle baslamali.");
        if (s.Length > 100)
            throw new ArgumentException("WidgetCode en fazla 100 karakter olabilir.");
        return s;
    }

    private static bool IsValidDataType(string dt) =>
        dt == "text" || dt == "numeric" || dt == "date" || dt == "boolean" ||
        dt == "dropdown" || dt == "multi-select" || dt == "group" ||
        dt == "link" || dt == "lookup" || dt == "grid" || dt == "guide-list";

    // ══════════════════════════════════════════════════════════
    // Save values (Faz A — degisiklik yok)
    // ══════════════════════════════════════════════════════════

    public async Task SaveValuesAsync(SaveWidgetValuesRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.FormId <= 0) throw new ArgumentException("FormId gecersiz.");
        if (string.IsNullOrWhiteSpace(request.RecordId)) throw new ArgumentException("RecordId bos olamaz.");

        var widgets = await _repository.GetWidgetsByFormAsync(request.FormId, ct);
        var widgetsByCode = widgets.ToDictionary(w => w.WidgetCode, StringComparer.OrdinalIgnoreCase);

        // widgetId → serialized string
        var serialized = new Dictionary<int, string?>();

        foreach (var kv in request.Values)
        {
            if (!widgetsByCode.TryGetValue(kv.Key, out var widget))
            {
                // Tanimsiz widget kodu — sessiz gec veya hata firlat; suan sessiz
                continue;
            }
            if (!widget.IsActive
                || string.Equals(widget.DataType, "group",      StringComparison.OrdinalIgnoreCase)
                || string.Equals(widget.DataType, "grid",       StringComparison.OrdinalIgnoreCase)
                || string.Equals(widget.DataType, "guide-list", StringComparison.OrdinalIgnoreCase))
            {
                // Grup, grid ve guide-list (salt okunur akordion liste) WidgetTra'ya yazilmaz.
                // Grid child satirlari SaveRecordAsync master-detail akisinda saglanir.
                // Guide-list ise hic deger uretmez (sadece okuma widget'i).
                continue;
            }

            var raw = kv.Value;
            if (raw == null)
            {
                serialized[widget.Id] = null;
                continue;
            }

            string? asString = SerializeAndValidate(widget, raw);
            serialized[widget.Id] = asString;
        }

        await _repository.UpsertValuesAsync(
            request.FormId,
            request.RecordId,
            serialized,
            ct,
            request.ParentRecordId);
    }

    // ══════════════════════════════════════════════════════════
    // Faz E — Master-Detail save orkestrasyonu
    // ══════════════════════════════════════════════════════════

    public async Task<SaveRecordResponseDto> SaveRecordAsync(
        int formId,
        string recordId,
        SaveRecordRequest request,
        CancellationToken ct)
    {
        if (formId <= 0) throw new ArgumentException("FormId gecersiz.");
        if (string.IsNullOrWhiteSpace(recordId)) throw new ArgumentException("RecordId bos olamaz.");
        if (request == null) throw new ArgumentNullException(nameof(request));

        // 1) Parent kaydet — values null olabilir (sadece grid guncellemesi durumu)
        var parentValues = request.Values ?? new Dictionary<string, object?>();
        await SaveValuesAsync(new SaveWidgetValuesRequest(formId, recordId, parentValues), ct);

        // 2) Master form'un widget'larini cek — grid widgetCode → metadata resolve icin
        var masterWidgets = await _repository.GetWidgetsByFormAsync(formId, ct);
        var masterWidgetByCode = masterWidgets
            .ToDictionary(w => w.WidgetCode, StringComparer.OrdinalIgnoreCase);

        var gridsNormalized = new Dictionary<string, IReadOnlyCollection<SaveGridRowNormalized>>(
            StringComparer.OrdinalIgnoreCase);

        if (request.Grids != null && request.Grids.Count > 0)
        {
            foreach (var gridEntry in request.Grids)
            {
                var gridWidgetCode = gridEntry.Key;
                var payload = gridEntry.Value;

                if (!masterWidgetByCode.TryGetValue(gridWidgetCode, out var gridWidget))
                    throw new ArgumentException($"Master form'da grid widget bulunamadi: '{gridWidgetCode}'");
                if (!string.Equals(gridWidget.DataType, "grid", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Widget '{gridWidgetCode}' grid tipi degil.");

                // OptionsJson'dan childFormCode cikar ve payload ile dogrula
                var meta = ParseMetadata(gridWidget.OptionsJson);
                var definedChild = meta != null && meta.TryGetValue("childFormCode", out var c) ? c : null;
                if (string.IsNullOrWhiteSpace(definedChild))
                    throw new ArgumentException($"Grid widget '{gridWidgetCode}' icin childFormCode tanimli degil.");
                if (!string.Equals(definedChild, payload.ChildFormCode, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"childFormCode uyumsuz: widget '{definedChild}' tanimli, istek '{payload.ChildFormCode}' gonderdi.");

                // Child form'u resolve et
                var childForm = await _repository.GetFormByCodeAsync(payload.ChildFormCode, ct);
                if (childForm == null)
                    throw new ArgumentException($"Child form bulunamadi: '{payload.ChildFormCode}'");

                // 3) Mevcut child RecordId setini oku (orphan tespiti icin)
                var existingChildIds = await _repository.GetChildRecordIdsAsync(childForm.Id, recordId, ct);
                var existingSet = new HashSet<string>(existingChildIds, StringComparer.OrdinalIgnoreCase);

                // 4) Her child satir icin kaydet (varsa update, yoksa insert)
                var keptSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var normalized = new List<SaveGridRowNormalized>(payload.Rows.Count);

                foreach (var row in payload.Rows)
                {
                    var childRecordId = string.IsNullOrWhiteSpace(row.RecordId)
                        ? GenerateChildRecordId(recordId)
                        : row.RecordId.Trim();

                    // Nested SaveValuesAsync — ParentRecordId ile
                    await SaveValuesAsync(
                        new SaveWidgetValuesRequest(
                            childForm.Id,
                            childRecordId,
                            row.Values ?? new Dictionary<string, object?>(),
                            ParentRecordId: recordId),
                        ct);

                    keptSet.Add(childRecordId);
                    normalized.Add(new SaveGridRowNormalized(
                        OriginalRecordId: row.RecordId,
                        RecordId: childRecordId));
                }

                // 5) Orphan temizligi — existing - kept = silinecek eski satirlar
                var orphans = existingSet.Except(keptSet, StringComparer.OrdinalIgnoreCase).ToArray();
                if (orphans.Length > 0)
                {
                    await _repository.DeleteChildRecordsAsync(childForm.Id, orphans, ct);
                }

                gridsNormalized[gridWidgetCode] = normalized;
            }
        }

        return new SaveRecordResponseDto(
            Success: true,
            FormId: formId,
            RecordId: recordId,
            Grids: gridsNormalized);
    }

    /// <summary>
    /// Yeni bir child RecordId uretir — parentRecordId prefix'i + 12 hex char.
    /// Garantili unique: Guid.NewGuid kullaniyoruz (12 hex = 48 bit, pratikte carpisma yok).
    /// Max uzunluk: parent (60) + "_" + 12 = 73 → NVARCHAR(60) kolonu icin kisaltilmasi gerekebilir.
    /// Bu yuzden parent kismini guvenli uzunlukta kirpiyoruz (max 47 char + "_" + 12 = 60).
    /// </summary>
    private static string GenerateChildRecordId(string parentRecordId)
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
        var parentPart = (parentRecordId ?? "p").Trim();
        if (parentPart.Length > 47) parentPart = parentPart.Substring(0, 47);
        return $"{parentPart}_{suffix}";
    }

    // ══════════════════════════════════════════════════════════
    // DataType-aware helpers
    // ══════════════════════════════════════════════════════════

    private static IReadOnlyCollection<string>? ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return null;
        var trimmed = optionsJson.TrimStart();
        if (trimmed.StartsWith("{")) return null;   // object shape → ParseMetadata kullanir
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(optionsJson);
            return arr ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// OptionsJSON object shape'ini ({"guideCode":"..."} gibi) parse eder.
    /// Sadece lookup (ve ileride benzer) tipler icin dolu doner; dropdown/
    /// multi-select/link icin null doner (onlar ParseOptions ile array'den okur).
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ParseMetadata(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return null;
        var trimmed = optionsJson.TrimStart();
        if (!trimmed.StartsWith("{")) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(optionsJson);
            return dict;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════
    // Faz G — Rules (visibleIf / disabledIf / formula)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Guvenli karakter seti: harfler, rakamlar, altcizgi, whitespace, operatorler,
    /// parantez, string/tirnak, karsilastirma. JavaScript'i exec edebilecek karakterler
    /// (; : => gibi) kasitli olarak DISARIDA.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex RuleCharsRegex =
        new(@"^[\p{L}\p{N}_ +\-*/%().,<>=!&|?:'""\s]*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// JS/CSS injection riskini mitigate eden yasakli anahtar kelimeler.
    /// Backend parse etmiyor (frontend expr-eval tek kaynak) ama bu kelimelerden
    /// biri stringin icinde gorunuyorsa admin hatali bir sey yaziyordur → reddet.
    /// </summary>
    private static readonly string[] ForbiddenKeywords =
    {
        "eval", "function", "=>", "await", "async", "import", "require",
        "window", "document", "globalThis", "process", "this.", "new ",
        "constructor", "prototype", "__proto__", "Function"
    };

    private const int MaxRuleLength = 500;

    /// <summary>
    /// Admin'den gelen rule stringlerini temizler ve guvenli bir sozluge cevirir.
    /// Hicbir slot dolu degilse null doner (saklamaya deger birsey yok).
    /// Gecersiz karakter veya yasakli kelime varsa ArgumentException firlatir.
    /// </summary>
    private static Dictionary<string, string>? SanitizeRules(WidgetRulesDto rules)
    {
        var dict = new Dictionary<string, string>();

        void AddIfValid(string key, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return;
            var trimmed = expr.Trim();
            if (trimmed.Length > MaxRuleLength)
                throw new ArgumentException($"Kural ifadesi fazla uzun ({trimmed.Length} > {MaxRuleLength}): {key}");
            if (!RuleCharsRegex.IsMatch(trimmed))
                throw new ArgumentException($"Kural ifadesi gecersiz karakter iceriyor: {key}");
            foreach (var kw in ForbiddenKeywords)
            {
                if (trimmed.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new ArgumentException($"Kural ifadesinde yasakli kelime: {key} → '{kw}'");
            }
            dict[key] = trimmed;
        }

        AddIfValid("visibleIf",  rules.VisibleIf);
        AddIfValid("disabledIf", rules.DisabledIf);
        AddIfValid("requiredIf", rules.RequiredIf);
        AddIfValid("formula",    rules.Formula);

        return dict.Count == 0 ? null : dict;
    }

    /// <summary>
    /// DB'deki RulesJSON string'ini WidgetRulesDto'ya parse eder. Null veya
    /// kirik JSON ise null doner — frontend hic Rules field'i gormez.
    /// </summary>
    private static WidgetRulesDto? ParseRules(string? rulesJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(rulesJson);
            if (dict == null || dict.Count == 0) return null;
            dict.TryGetValue("visibleIf",  out var vi);
            dict.TryGetValue("disabledIf", out var di);
            dict.TryGetValue("requiredIf", out var ri);
            dict.TryGetValue("formula",    out var fm);
            if (string.IsNullOrWhiteSpace(vi) && string.IsNullOrWhiteSpace(di) &&
                string.IsNullOrWhiteSpace(ri) && string.IsNullOrWhiteSpace(fm))
                return null;
            return new WidgetRulesDto(vi, di, fm, ri);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// DB'den gelen string Value'yu, DataType'a gore React'in bekledigi
    /// tipe (decimal/bool/string[]/string) parse eder.
    /// </summary>
    private static object? ParseValueForRender(string? raw, string dataType)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        switch (dataType.ToLowerInvariant())
        {
            case "numeric":
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    return d;
                return raw;

            case "date":
                // Saklama formati "yyyy-MM-dd"; olduğu gibi döneriz (React ISO bekler)
                return raw;

            case "boolean":
                if (bool.TryParse(raw, out var b)) return b;
                return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);

            case "multi-select":
                try
                {
                    return JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>();
                }
                catch (JsonException)
                {
                    return raw;
                }

            case "group":
                return null;

            default: // text, dropdown, custom
                return raw;
        }
    }

    /// <summary>
    /// Kullanicinin gonderdigi object?'i DataType'a gore validate edip
    /// DB'ye yazilacak string formatina cevirir.
    /// </summary>
    private static string? SerializeAndValidate(WidgetDefinition widget, object rawValue)
    {
        switch (widget.DataType.ToLowerInvariant())
        {
            case "text":
            case "dropdown":
            case "link":
            case "lookup":
            {
                var s = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
                if (widget.MaxLength.HasValue && s.Length > widget.MaxLength.Value)
                {
                    throw new ArgumentException(
                        $"'{widget.Label}' en fazla {widget.MaxLength.Value} karakter olmali (girilen {s.Length}).");
                }
                if (widget.MinLength.HasValue && s.Length > 0 && s.Length < widget.MinLength.Value)
                {
                    throw new ArgumentException(
                        $"'{widget.Label}' en az {widget.MinLength.Value} karakter olmali (girilen {s.Length}).");
                }
                if (widget.ExpectedLength.HasValue && s.Length > 0 && s.Length != widget.ExpectedLength.Value)
                {
                    throw new ArgumentException(
                        $"'{widget.Label}' tam {widget.ExpectedLength.Value} karakter olmali (girilen {s.Length}).");
                }
                return string.IsNullOrEmpty(s) ? null : s;
            }

            case "numeric":
            {
                var s = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(s)) return null;
                if (!decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                {
                    throw new ArgumentException($"'{widget.Label}' gecerli bir sayi olmali (girilen '{s}').");
                }
                if (widget.MinValue.HasValue && d < widget.MinValue.Value)
                {
                    throw new ArgumentException(
                        $"'{widget.Label}' en kucuk deger: {widget.MinValue.Value} (girilen {d}).");
                }
                if (widget.MaxValue.HasValue && d > widget.MaxValue.Value)
                {
                    throw new ArgumentException(
                        $"'{widget.Label}' en buyuk deger: {widget.MaxValue.Value} (girilen {d}).");
                }
                return d.ToString(CultureInfo.InvariantCulture);
            }

            case "date":
            {
                var s = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(s)) return null;
                if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    throw new ArgumentException($"'{widget.Label}' gecerli bir tarih olmali (girilen '{s}').");
                }
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            case "boolean":
            {
                if (rawValue is bool b) return b ? "true" : "false";
                var s = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
                if (bool.TryParse(s, out var b2)) return b2 ? "true" : "false";
                if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
                if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
                throw new ArgumentException($"'{widget.Label}' gecerli bir dogru/yanlis degeri olmali.");
            }

            case "multi-select":
            {
                // Array olarak gelebilir (JSON'dan parse sonrasi List<object> veya JsonElement)
                if (rawValue is System.Text.Json.JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<string>();
                        foreach (var el in je.EnumerateArray())
                        {
                            list.Add(el.ToString() ?? string.Empty);
                        }
                        return JsonSerializer.Serialize(list);
                    }
                    if (je.ValueKind == JsonValueKind.String)
                    {
                        var s = je.GetString();
                        return string.IsNullOrEmpty(s)
                            ? null
                            : JsonSerializer.Serialize(new[] { s });
                    }
                    return null;
                }
                if (rawValue is IEnumerable<object> enumerable)
                {
                    var list = enumerable
                        .Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? string.Empty)
                        .ToArray();
                    return JsonSerializer.Serialize(list);
                }
                // Tek deger olarak geldiyse array'e sar
                var single = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                return string.IsNullOrEmpty(single)
                    ? null
                    : JsonSerializer.Serialize(new[] { single });
            }

            case "group":
                return null; // grup basligina deger atanmaz

            default:
                // Bilinmeyen tip — string olarak kaydet
                return Convert.ToString(rawValue, CultureInfo.InvariantCulture);
        }
    }
}
