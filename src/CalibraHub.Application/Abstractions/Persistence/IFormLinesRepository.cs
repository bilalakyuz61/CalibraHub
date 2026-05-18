namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Master-Detail entegrasyonun "kalem" verilerini ceken servis.
///
/// Bir form'un LinesFormCode'u dolu ise (orn. SALES_ORDER_NEW.LinesFormCode = 'SALES_ORDER_LINES'),
/// IntegrationRunner header kaydini cektikten sonra bu servis ile baglı kalem
/// satirlarini ceker. Veriyi v_Flat_{linesFormCode} view'indan okur — bu view
/// kalem tablosunun temel kolonlari + WidgetTra'dan pivot edilmis widget degerlerini
/// tek satirda flat olarak doner (header form'unun v_Flat_*'ı ile ayni mantik).
///
/// Kullanim:
///   var lines = await _linesRepo.GetLinesAsync(
///       linesFormCode:   "SALES_ORDER_LINES",
///       parentColumn:    "DocumentId",          // kalem tablosunda parent FK
///       parentRecordId:  documentId.ToString(), // header'in PK degeri
///       ct);
///   foreach (var line in lines) {
///       var stokKodu = line["StokKodu"];   // dictionary access
///       var miktar   = line["Quantity"];
///       ...
///   }
/// </summary>
public interface IFormLinesRepository
{
    /// <summary>
    /// Bir parent record'a baglı tum kalem satirlarini doner.
    /// Her satir flat dictionary (kolon adı → deger).
    /// View yoksa veya bulamazsa bos liste doner (exception throw etmez).
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetLinesAsync(
        string linesFormCode,
        string parentColumn,
        string parentRecordId,
        CancellationToken ct);
}
