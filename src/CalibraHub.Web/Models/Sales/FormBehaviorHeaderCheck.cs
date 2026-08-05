using System.Text.Json;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Rules;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Web.Models.Sales;

/// <summary>
/// Form Davranış Katmanı — SaveDocument server-side zorunluluk denetimi (2026-08-05).
///
/// Client validasyonunun (formBehavior.js) sunucu paritesi. Yalnızca sunucunun
/// DTO'dan GÜVENLE doğrulayabildiği alanlar denetlenir (nullable/string alanlar);
/// bool/numeric alanlar (vatIncluded, discountRate) "boşluk" kavramı taşımadığı
/// için kontrol dışıdır. requiredIf/visibleIf kuralları sunucu scope'unun
/// kurabildiği alt kümede değerlendirilir; scope'ta olmayan tanımlayıcı içeren
/// kural fail-open çalışır (eksik ENFORCEMENT olabilir ama asla YANLIŞ BLOKLAMA
/// olmaz — kullanıcı kaydı haksız yere engellenmez).
/// </summary>
public static class FormBehaviorHeaderCheck
{
    public static IReadOnlyList<string> FindMissing(
        IReadOnlyCollection<FormFieldBehavior> behaviors,
        IReadOnlyList<DocumentHeaderFieldCatalog.HeaderField>? catalog,
        SaveDocumentRequest request,
        bool isOrder)
    {
        static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);

        // key → "değer dolu mu?" (yalnız güvenle doğrulanabilen alanlar)
        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["validUntil"]      = isOrder ? request.DeliveryDate.HasValue : request.ValidUntil.HasValue,
            ["customer"]        = request.ContactId.HasValue || Has(request.ContactCode),
            ["salesRep"]        = request.SalesRepId.HasValue,
            ["requester"]       = request.RequesterPersonnelId.HasValue,
            ["location"]        = request.LocationId.HasValue,
            ["paymentTerms"]    = Has(request.PaymentTerms),
            ["deliveryTerms"]   = Has(request.DeliveryTerms),
            ["deliveryAddress"] = Has(request.DeliveryAddress),
            ["notes"]           = Has(request.Notes),
        };

        // Kural scope'u — client scope'unun sunucuda kurulabilen alt kümesi.
        var scope = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["vatIncluded"]     = request.IsVatIncluded,
            ["discountRate"]    = (double)request.DiscountRate,
            ["currencyId"]      = (double)request.CurrencyId,
            ["paymentTerms"]    = request.PaymentTerms ?? "",
            ["deliveryTerms"]   = request.DeliveryTerms ?? "",
            ["deliveryAddress"] = request.DeliveryAddress ?? "",
            ["notes"]           = request.Notes ?? "",
        };

        var labels = (catalog ?? Array.Empty<DocumentHeaderFieldCatalog.HeaderField>())
            .ToDictionary(c => c.Key, c => c.Label, StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var b in behaviors)
        {
            if (b.FieldKey.StartsWith("tab:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!values.TryGetValue(b.FieldKey, out var hasValue)) continue; // doğrulanamayan alan → client'a bırak
            if (!b.IsVisible) continue; // gizli alan zorunlu sayılmaz

            var visibleIf = TryGetRule(b.RulesJson, "visibleIf");
            if (visibleIf is not null && !RuleExpr.EvaluateBool(visibleIf, scope))
            {
                // Kural scope'ta değerlendirilebildi ve "gizli" dedi → zorunluluk atla.
                // (Eval hatası RuleExpr'de false döner; o durumda da atlamak yanlış
                // bloklamayı önler — bilinçli fail-open.)
                continue;
            }

            var required = b.IsRequired;
            if (!required)
            {
                var requiredIf = TryGetRule(b.RulesJson, "requiredIf");
                if (requiredIf is not null)
                    required = RuleExpr.EvaluateBool(requiredIf, scope);
            }

            if (required && !hasValue)
                missing.Add(b.LabelText ?? (labels.TryGetValue(b.FieldKey, out var l) ? l : b.FieldKey));
        }
        return missing;
    }

    /// <summary>
    /// Kalem (satır) zorunluluk denetimi. Sunucunun DTO'dan güvenle doğrulayabildiği
    /// kolonlar: unitId (null = boş) ve unitPrice (0 = boş — zorunluysa pozitif
    /// beklenir). quantity zaten FluentValidation'da pozitif-zorunlu; computed/readonly
    /// kolonlar (lineTotal, taxRate, materialName) kontrol dışı. Kural scope'u satır
    /// bazlıdır (quantity/unitPrice/discountRate/notes).
    /// </summary>
    public static IReadOnlyList<string> FindMissingLineFields(
        IReadOnlyCollection<FormFieldBehavior> behaviors,
        SaveDocumentRequest request)
    {
        var relevant = behaviors
            .Where(b => !b.FieldKey.StartsWith("tab:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (relevant.Length == 0) return Array.Empty<string>();

        var labels = (DocumentLineFieldCatalog.Resolve("SALES_QUOTE_LINES") ?? [])
            .ToDictionary(c => c.Key, c => c.Label, StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        var idx = 0;
        foreach (var line in request.Lines ?? Array.Empty<SaveDocumentLineRequest>())
        {
            idx++;
            var scope = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["quantity"]     = (double)line.Quantity,
                ["unitPrice"]    = (double)line.UnitPrice,
                ["discountRate"] = (double)line.DiscountRate,
                ["notes"]        = line.Notes ?? "",
            };
            foreach (var b in relevant)
            {
                bool? hasValue = b.FieldKey.ToLowerInvariant() switch
                {
                    "unitid"    => line.UnitId.HasValue,
                    "unitprice" => line.UnitPrice > 0m,
                    _           => null, // doğrulanamayan kolon → client'a bırak
                };
                if (hasValue is null) continue;
                if (!b.IsVisible) continue;

                var visibleIf = TryGetRule(b.RulesJson, "visibleIf");
                if (visibleIf is not null && !RuleExpr.EvaluateBool(visibleIf, scope)) continue;

                var required = b.IsRequired;
                if (!required)
                {
                    var requiredIf = TryGetRule(b.RulesJson, "requiredIf");
                    if (requiredIf is not null) required = RuleExpr.EvaluateBool(requiredIf, scope);
                }
                if (required && hasValue == false)
                {
                    var label = b.LabelText ?? (labels.TryGetValue(b.FieldKey, out var l) ? l : b.FieldKey);
                    missing.Add($"Satır {idx}: {label}");
                }
            }
        }
        return missing;
    }

    private static string? TryGetRule(string? rulesJson, string key)
    {
        if (string.IsNullOrWhiteSpace(rulesJson)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(rulesJson);
            return dict != null && dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        }
        catch (JsonException) { return null; }
    }
}
