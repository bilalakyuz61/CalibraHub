using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Cari/Stok kod üretici. Aktif kuralları priority sırasında tarar, şart eşleşen
/// ilk kuralın template'ini token'larla doldurup üretir. Çakışma olursa suffix
/// (-A, -B, ...) ekler. Tasarım > Tasarım Kuralları altındaki tab'lardan yönetilir.
/// </summary>
public interface ICodeGeneratorService
{
    /// <summary>
    /// Verilen entityType ('Contact'|'Item') için kod üretir. FieldValues + WidgetValues
    /// token referansı için kullanılır. Hiç kural eşleşmezse Success=false döner.
    /// </summary>
    Task<GenerateCodeResult> GenerateAsync(GenerateCodeRequest request, CancellationToken ct);
}
