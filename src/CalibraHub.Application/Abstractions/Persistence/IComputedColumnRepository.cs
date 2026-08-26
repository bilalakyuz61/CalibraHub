using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Hesaplanan Kolon tanımlarının deposu + kaynak view keşfi ve değer okuma.
///
/// Tanımlayıcı doğrulaması BU KATMANDA yapılır (sys.views / sys.columns). Doğrulanmamış bir
/// ad hiçbir sorguya konmaz — bu, kurgunun güvenlik temeli.
/// </summary>
public interface IComputedColumnRepository
{
    Task<IReadOnlyList<ComputedColumnDto>> GetAllAsync(CancellationToken ct);

    /// <summary>Bir listede kullanılabilir tanımlar: varlığa uyan + aktif + board kısıtına uyan.</summary>
    Task<IReadOnlyList<ComputedColumnDto>> GetForBoardAsync(string entityKind, string boardKey, CancellationToken ct);

    Task<int> SaveAsync(SaveComputedColumnRequest request, int? userId, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Seçilebilecek view'lar ve kolonları (yalnız view; tablo DEĞİL).</summary>
    Task<IReadOnlyList<ComputedColumnSourceDto>> GetSourcesAsync(CancellationToken ct);

    /// <summary>Kaydetmeden önce deneme okuması (ilk N satır + süre).</summary>
    Task<ComputedColumnPreviewDto> PreviewAsync(SaveComputedColumnRequest request, int sampleSize, CancellationToken ct);

    /// <summary>
    /// Verilen anahtarlar için değerleri okur (liste sayfası için TEK sorgu).
    /// Anahtar listesi SAYFADAKİ satırlarla sınırlıdır — tüm tabloyu taramak yerine
    /// yalnız ekranda görünen kayıtlar sorgulanır.
    ///
    /// Tanım bozulmuşsa (view silinmiş, kolon kaldırılmış) exception fırlatmaz: boş sözlük
    /// döner ve hata metni ikinci değerde gelir. Tek bir kolon yüzünden liste ekranı çökmemeli.
    /// </summary>
    Task<(IReadOnlyDictionary<int, ComputedCellValue> Values, string? Error)> ReadValuesAsync(
        ComputedColumnDto column, IReadOnlyCollection<int> keys, CancellationToken ct);
}

/// <summary>Tek hücrenin ham değeri + (varsa) birimi. Biçimlendirme üst katmanda yapılır.</summary>
public sealed record ComputedCellValue(string? Value, string? Unit);
