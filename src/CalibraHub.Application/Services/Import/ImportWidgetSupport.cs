using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// İçe-aktarım handler'ları için ortak EAV widget (özel alan) desteği.
/// ContactImportHandler'daki pilot pattern'in genelleştirilmiş hali — handler
/// bunu compose eder; PreloadAsync / GetFields / CommitRow'a birer satırlık
/// delege yeterlidir:
///
///   _widgetSupport = new ImportWidgetSupport(widgetRepo, widgetService, "ITEMS");
///   PreloadAsync   → await _widgetSupport.PreloadAsync(ct);
///   GetFields      → fields.AddRange(_widgetSupport.GetFields());
///   CommitRowAsync → await _widgetSupport.SaveRowValuesAsync(id.ToString(), row, ct);
///
/// RecordId konvansiyonu: entity'nin INT PK'sının string hali — runtime
/// DynamicWidgetRenderer'ın aynı form için kullandığı recordId ile birebir
/// aynı olmalı (Contact: Contact.Id, Item: Items.Id). Aksi halde import ile
/// yazılan değerler edit ekranında görünmez.
/// </summary>
public sealed class ImportWidgetSupport
{
    private readonly IWidgetRepository _widgetRepo;
    private readonly IWidgetService _widgetService;
    private readonly string _formCode;

    private FormDefinition? _form;
    private List<WidgetDefinition>? _widgets;

    public ImportWidgetSupport(IWidgetRepository widgetRepo, IWidgetService widgetService, string formCode)
    {
        _widgetRepo    = widgetRepo;
        _widgetService = widgetService;
        _formCode      = formCode;
    }

    /// <summary>Form + aktif custom widget tanımlarını yükler (run-cache, idempotent).</summary>
    public async Task PreloadAsync(CancellationToken ct)
    {
        if (_widgets is not null) return;
        _form = await _widgetRepo.GetFormByCodeAsync(_formCode, ct);
        _widgets = _form is null
            ? new List<WidgetDefinition>()
            : (await _widgetRepo.GetWidgetsByFormAsync(_form.Id, ct))
                .Where(w => !w.IsSystemField && w.IsActive && !IsContainerType(w.DataType)
                    // attachment import ile taşınamaz (değer = dosya Id'si; Excel hücresine yazılamaz)
                    && !string.Equals(w.DataType, "attachment", StringComparison.OrdinalIgnoreCase))
                .OrderBy(w => w.SortOrder)
                .ToList();
    }

    /// <summary>İçe-aktarım kolonu olarak sunulacak widget alanları (PreloadAsync sonrası).</summary>
    public IReadOnlyList<ImportTargetFieldDto> GetFields()
        => _widgets is null
            ? Array.Empty<ImportTargetFieldDto>()
            : _widgets.Select(WidgetToField).ToList();

    /// <summary>
    /// Satırda dolu widget kolonu var mı? — insert akışında yeni kaydın Id'sini
    /// bulmak ek sorgu gerektirir; hiç widget değeri yoksa o maliyete girilmez.
    /// </summary>
    public bool HasRowValues(IReadOnlyDictionary<string, string?> row)
        => _widgets is { Count: > 0 }
           && _widgets.Any(w => row.TryGetValue(w.WidgetCode, out var v) && !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// Satırdaki eşleştirilmiş widget kolonlarını WidgetTra'ya yazar.
    /// EnforceRequired=false — satır kısmi dict taşır; form'daki diğer zorunlu
    /// widget'lar bu akışta aranmaz. Dönüş: null = başarılı/yazacak değer yok,
    /// aksi halde kullanıcıya gösterilecek hata mesajı (ana kayıt kaydedilmiştir).
    /// </summary>
    public async Task<string?> SaveRowValuesAsync(
        string recordId,
        IReadOnlyDictionary<string, string?> row,
        CancellationToken ct)
    {
        if (_form is null || _widgets is not { Count: > 0 }) return null;
        if (string.IsNullOrWhiteSpace(recordId)) return null;

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in _widgets)
        {
            if (!row.TryGetValue(w.WidgetCode, out var v)) continue;
            if (string.IsNullOrWhiteSpace(v)) continue;
            values[w.WidgetCode] = NormalizeForDataType(v.Trim(), w.DataType);
        }
        if (values.Count == 0) return null;

        try
        {
            await _widgetService.SaveValuesAsync(
                new SaveWidgetValuesRequest(_form.Id, recordId, values, EnforceRequired: false), ct);
            return null;
        }
        catch (Exception ex)
        {
            return $"Özel alanlar kaydedilemedi: {ex.Message}";
        }
    }

    // ── Yardımcılar ────────────────────────────────────────────────

    private static bool IsContainerType(string? dt) => dt is "group" or "grid" or "guide-list";

    /// <summary>
    /// Excel hücre değerini SerializeAndValidate'in beklediği forma çevirir:
    /// boolean için TR/EN serbest metin ("evet", "x", "1") → "true"/"false",
    /// numeric için Türkçe ondalık virgül → nokta. Diğer tipler ham kalır
    /// (tip doğrulama servis tarafında yapılır, hata satır sonucuna düşer).
    /// </summary>
    private static object NormalizeForDataType(string raw, string? dataType)
    {
        switch ((dataType ?? "").ToLowerInvariant())
        {
            case "boolean":
            {
                var v = raw.ToLowerInvariant();
                return (v is "1" or "true" or "evet" or "e" or "x" or "var" or "yes" or "✓") ? "true" : "false";
            }
            case "numeric":
            {
                var v = raw.Replace(" ", "");
                if (v.Contains(',') && !v.Contains('.')) v = v.Replace(',', '.');
                else if (v.Contains(',') && v.Contains('.')) v = v.Replace(".", "").Replace(',', '.');
                return v;
            }
            default:
                return raw;
        }
    }

    private static ImportTargetFieldDto WidgetToField(WidgetDefinition w) =>
        new(w.WidgetCode, w.Label, MapWidgetType(w.DataType), w.IsRequired, false,
            $"Özel alan: {w.Label}", ParseOptions(w.OptionsJson));

    private static string MapWidgetType(string? dt) => dt switch
    {
        "numeric" => "decimal",
        "date"    => "date",
        "boolean" => "bool",
        _         => "string",
    };

    // OptionsJson → açılır liste değerleri. ["A","B"] veya [{"l":"Label","k":"key"}] formatları.
    // Object shape (lookup/grid metadata) → null (serbest metin).
    private static IReadOnlyList<string>? ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var trimmed = json.TrimStart();
        if (!trimmed.StartsWith("[")) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    // Legacy {"l":"Label"} / {"optionLabel":"Label"} çiftleri
                    if (el.TryGetProperty("l", out var l) && l.ValueKind == JsonValueKind.String)
                        list.Add(l.GetString() ?? "");
                    else if (el.TryGetProperty("optionLabel", out var ol) && ol.ValueKind == JsonValueKind.String)
                        list.Add(ol.GetString() ?? "");
                }
            }
            list.RemoveAll(string.IsNullOrWhiteSpace);
            return list.Count > 0 ? list : null;
        }
        catch (JsonException) { return null; }
    }
}
