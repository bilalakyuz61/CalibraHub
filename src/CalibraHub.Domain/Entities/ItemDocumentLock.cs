namespace CalibraHub.Domain.Entities;

/// <summary>
/// Planlama: bir malzemenin belirli bir belge tipinde seçilmesini engelleyen kilit.
/// Kilit varsa, o belge tipinin malzeme lookup'ında bu malzeme listelenmez
/// ("o belgede seçilemesin" kuralı). DocType, sistem genelindeki belge tipi
/// kodudur (satis_teklifi, depo_giris, sayim vb.).
/// </summary>
public sealed class ItemDocumentLock
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public string DocType { get; init; } = string.Empty;
}
