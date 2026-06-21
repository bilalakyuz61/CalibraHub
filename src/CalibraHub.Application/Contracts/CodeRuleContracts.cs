namespace CalibraHub.Application.Contracts;

/// <summary>
/// Cari/Stok kod türetme kural DTO'ları + servis sözleşmeleri.
/// EntityType: "Contact" | "Item".
/// </summary>
public sealed record CodeRuleDto(
    int Id,
    string EntityType,
    string Name,
    string Template,
    int Priority,
    int ResetPeriod,
    bool IsActive,
    DateTime Created,
    DateTime? Updated,
    IReadOnlyList<CodeRuleConditionDto> Conditions
);

public sealed record CodeRuleConditionDto(
    int Id,
    string FieldType,   // 'Field' | 'Widget'
    string FieldName,
    string Operator,    // '=', '!=', 'in', 'notin', 'startsWith', 'isNull', 'isNotNull'
    string? Value
);

/// <summary>Form post payload — kayıt + güncelleme ortak.</summary>
public sealed class SaveCodeRuleRequest
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "Contact";
    public string Name { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int ResetPeriod { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SaveCodeRuleConditionRequest> Conditions { get; set; } = new();
}

public sealed class SaveCodeRuleConditionRequest
{
    public string FieldType { get; set; } = "Field";
    public string FieldName { get; set; } = string.Empty;
    public string Operator { get; set; } = "=";
    public string? Value { get; set; }
}

/// <summary>
/// Runtime'da kod üretme isteği. Manuel form veya Excel import'tan gelir.
/// FieldValues = Contact/Item DB kolonları (City, GroupId vb.).
/// WidgetValues = WidgetKey → Value (form widget'larından — yeni kayıt için RAM'de, kayıt sonrası WidgetTra'dan).
/// </summary>
public sealed class GenerateCodeRequest
{
    public string EntityType { get; set; } = "Contact";
    public Dictionary<string, string?> FieldValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> WidgetValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Üretim sonucu — kod üretildi mi, hangi kural ile, çakışma denemesi var mı.</summary>
public sealed record GenerateCodeResult(
    bool Success,
    string? Code,
    int? AppliedRuleId,
    string? AppliedRuleName,
    string? Error,
    int SuffixAttempts
);
