namespace CalibraHub.Web.Models.Admin;

/// <summary>
/// 2026-06-13 — Veri Görünürlük Kuralları SmartBoard (C-Grid) liste view'ı için board config taşıyıcı.
/// Departmanlar / ScheduledTasks SmartBoard pattern'i ile aynı: tek <see cref="BoardConfig"/> alanı.
/// </summary>
public sealed class DataVisibilityRuleListViewModel
{
    public object BoardConfig { get; set; } = new();
}

/// <summary>
/// 2026-06-13 — Veri Görünürlük Kuralı düzenleme (tam sayfa, tablı) view modeli.
/// </summary>
public sealed class DataVisibilityRuleEditViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Kuralın bağlı olduğu entity FormCode (örn. CONTACTS).</summary>
    public string FormCode { get; set; } = string.Empty;

    /// <summary>0 = Kolon, 1 = Widget.</summary>
    public int FieldKind { get; set; }

    /// <summary>Kısıtlanacak alan — kolon adı veya WidgetCode.</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>Karşılaştırma operatörü (eq, neq, between, in, like, isnull, …).</summary>
    public string Operator { get; set; } = "eq";

    /// <summary>Widget türünde WidgetMas.Id.</summary>
    public int? WidgetId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Operatöre göre kısıtlanan değer(ler). Düzenlemede mevcut değerlerle dolar.</summary>
    public List<string> Values { get; set; } = new();

    public bool IsNew => Id <= 0;

    /// <summary>Form dropdown — alt formlar filtrelenmiş, Türkçe ad gösterimi (Code = FormCode).</summary>
    public List<FormOption> FormOptions { get; set; } = new();

    public sealed record FormOption(string Code, string Name);
}
