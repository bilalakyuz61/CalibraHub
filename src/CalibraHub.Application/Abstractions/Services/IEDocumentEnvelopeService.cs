using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Cevrimdisi (ERP) ice aktarilmis bir e-belgenin zarf UBL XML'ini SONRADAN tamamlar.
///
/// <para><b>Neden var:</b> zarf XML'i uzun sure hic okunmadi (yanlis kolona bakildi:
/// XMLVERI bos, gercek veri XMLBYTES icinde ZIP'li). Bu yuzden ice aktarilmis on binlerce
/// belgede PayloadRaw yalnizca bir izleme JSON'u — ekranda resmi (GIB) goruntusu ve XML
/// sekmesi URETILEMIYOR. Belgeleri yeniden aktarmak yerine, ilk goruntulemede zarf
/// kaynaktan okunur ve kayda yazilir; sonraki acilislar veritabanindan gelir.</para>
///
/// <para>Asla firlatmaz: kaynak kapali/erisilemez oldugunda null doner, ekran kalem
/// verisinden urettigi fatura gorunumunu gostermeye devam eder.</para>
/// </summary>
public interface IEDocumentEnvelopeService
{
    Task<string?> TryFetchAndPersistXmlAsync(IncomingDocument document, CancellationToken ct);
}
