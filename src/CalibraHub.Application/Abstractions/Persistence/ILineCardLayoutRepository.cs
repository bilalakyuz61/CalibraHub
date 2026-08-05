using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Kalem kartı düzeni (LineCardLayout) erişimi. Per-company DB — form kodu başına
/// tek aktif düzen (UX_LineCardLayout_FormCode WHERE IsActive=1).
/// </summary>
public interface ILineCardLayoutRepository
{
    Task<LineCardLayout?> GetActiveAsync(string formCode, CancellationToken ct);
    Task UpsertAsync(LineCardLayout layout, CancellationToken ct);
    /// <summary>Düzeni kaldırır → grid varsayılan (hardcoded) düzenine döner.</summary>
    Task DeleteAsync(string formCode, CancellationToken ct);
}
