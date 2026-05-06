namespace CalibraHub.Application.Abstractions.Services;

public interface INumeratorService
{
    /// <summary>
    /// Sirket parametre ekraninda tanimli format mask'i + sayac sonraki degeri ile
    /// belge numarasi uretir. formCode parametreden mask'i okumak icin (orn. WORK_ORDER).
    /// defaultMask parametre tanimli degilse fallback (orn. "WO-{yyyy}-{seq:5}").
    /// </summary>
    Task<string> GetNextNumberAsync(string entityType, string formCode, string defaultMask, CancellationToken ct);
}
