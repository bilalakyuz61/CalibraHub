namespace CalibraHub.Application.Abstractions.DesignProvider;

/// <summary>
/// Bir belge basılırken kullanılacak en uygun DocLayout'un Id'sini döndürür.
/// Önce DocLayoutRule tablosundan en spesifik eşleşmeyi arar; bulamazsa
/// DocLayout.IsDefault flag'ine düşer.
/// </summary>
public interface IDesignProvider
{
    /// <summary>
    /// Eşleşme bulamazsa InvalidOperationException fırlatır (caller new engine'i zorunlu istiyorsa).
    /// </summary>
    Task<int> GetEffectiveLayoutIdAsync(DesignSelectionContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Eşleşme bulamazsa <c>null</c> döner (dispatcher / fallback senaryoları için).
    /// </summary>
    Task<int?> TryGetEffectiveLayoutIdAsync(DesignSelectionContext ctx, CancellationToken ct = default);
}
