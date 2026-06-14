namespace CalibraHub.Application.Workflow;

/// <summary>
/// Bir belgeye ait NCalc değerlendirme context'ini JSON snapshot olarak üretir.
/// Workflow instance başlatılırken çağrılır; snapshot ContextJson'a yazılır ve değişmez.
/// </summary>
public interface IDocumentContextBuilder
{
    Task<Dictionary<string, object?>> BuildContextAsync(int documentId, CancellationToken ct = default);
}
