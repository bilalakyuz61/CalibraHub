namespace CalibraHub.Application.Services.Rules;

/// <summary>
/// Paylaşımlı kural ifadesi yardımcısı (2026-08-05 — Form Davranış Katmanı).
///
/// Widget kural motorunun (WidgetService.SanitizeRules / EvaluateRuleBool) güvenlik
/// süzgeci ve NCalc değerlendirmesiyle AYNI davranış — standart alan davranışları
/// (FormFieldBehavior) da aynı süzgeç/paritede çalışsın diye ortak statik sınıfa
/// çıkarıldı. WidgetService'in kendi private kopyası şimdilik yerinde bırakıldı
/// (regresyon riski almamak için); yeni kod BU sınıfı kullanır.
/// </summary>
public static class RuleExpr
{
    /// <summary>
    /// Güvenli karakter seti: harfler, rakamlar, altçizgi, whitespace, operatörler,
    /// parantez, tırnak, karşılaştırma. JS exec edebilecek karakterler (; : =>) dışarıda.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex RuleCharsRegex =
        new(@"^[\p{L}\p{N}_ +\-*/%().,<>=!&|?:'""\s]*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] ForbiddenKeywords =
    {
        "eval", "function", "=>", "await", "async", "import", "require",
        "window", "document", "globalThis", "process", "this.", "new ",
        "constructor", "prototype", "__proto__", "Function"
    };

    private const int MaxRuleLength = 500;

    /// <summary>
    /// İfadeyi temizler: boş → null; geçersiz karakter / yasaklı kelime / aşırı uzunluk
    /// → ArgumentException (400'e çevrilir). Geçerliyse trim'lenmiş ifade döner.
    /// </summary>
    public static string? Sanitize(string key, string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        var trimmed = expr.Trim();
        if (trimmed.Length > MaxRuleLength)
            throw new ArgumentException($"Kural ifadesi fazla uzun ({trimmed.Length} > {MaxRuleLength}): {key}");
        if (!RuleCharsRegex.IsMatch(trimmed))
            throw new ArgumentException($"Kural ifadesi geçersiz karakter içeriyor: {key}");
        foreach (var kw in ForbiddenKeywords)
        {
            if (trimmed.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                throw new ArgumentException($"Kural ifadesinde yasaklı kelime: {key} → '{kw}'");
        }
        return trimmed;
    }

    /// <summary>
    /// Bool sonuçlu ifadeyi NCalc ile değerlendirir. Bilinmeyen tanımlayıcı, syntax
    /// veya tip hatası → false (fail-open — frontend rule engine paritesi).
    /// </summary>
    public static bool EvaluateBool(string expression, IReadOnlyDictionary<string, object?> scope)
    {
        try
        {
            var expr = new NCalc.Expression(expression);
            foreach (var kv in scope)
                expr.Parameters[kv.Key] = kv.Value;
            var result = expr.Evaluate();
            return result is bool b && b;
        }
        catch
        {
            return false;
        }
    }
}
