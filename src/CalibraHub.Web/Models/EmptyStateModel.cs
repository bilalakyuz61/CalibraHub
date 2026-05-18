namespace CalibraHub.Web.Models;

/// <summary>
/// _EmptyState.cshtml partial'i icin view model (rapor §6.4).
/// </summary>
public sealed class EmptyStateModel
{
    /// <summary>Lucide-style ikon adi: "inbox", "search", "folder" — varsayilan info circle.</summary>
    public string? Icon { get; init; } = "inbox";

    /// <summary>Buyuk baslik (zorunlu).</summary>
    public string Title { get; init; } = "Henüz kayıt yok";

    /// <summary>Aciklama metni — kullaniciya "ne yapayim?" cevabini ver.</summary>
    public string? Description { get; init; }

    /// <summary>Opsiyonel CTA butonu yazisi.</summary>
    public string? ActionLabel { get; init; }

    /// <summary>Opsiyonel CTA URL'si.</summary>
    public string? ActionUrl { get; init; }
}
