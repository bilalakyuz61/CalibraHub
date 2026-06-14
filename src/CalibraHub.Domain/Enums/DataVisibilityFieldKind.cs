namespace CalibraHub.Domain.Enums;

/// <summary>
/// 2026-06-12 — Veri görünürlük kuralının hedef alan türü.
/// </summary>
public enum DataVisibilityFieldKind
{
    /// <summary>Entity tablosunun gerçek bir kolonu/FK'si (örn. Contact.ContactGroupId). Filtre: doğrudan WHERE predikatı.</summary>
    Column = 0,

    /// <summary>EAV widget alanı (WidgetTra.Value). Filtre: WidgetTra ön-sorgusuyla yasaklı RecordId çözümü.</summary>
    Widget = 1,
}
