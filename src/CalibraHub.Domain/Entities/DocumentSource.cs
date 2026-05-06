using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Hedef belge (siparis/fatura) ile kaynak belge (teklif/siparis) arasi koprulu N-1 iz.
/// Ornek: 3 satis teklifi tek siparise birlestirildiginde 3 satir document_source olusur.
/// WorkOrderSource'in satis-belgesi muadili.
/// </summary>
[Description("Bir Document'in (siparis/fatura) hangi onceki Document(lar)dan turetildigini saklar. document_id hedef, source_document_id kaynak. Ayni hedefte coklu kaynak (cari bazli birlestirme) mumkun.")]
public sealed class DocumentSource
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Hedef belge (siparis/fatura). FK -> Document.Id")]
    public int DocumentId { get; init; }

    [Description("Kaynak belge (teklif/siparis). FK -> Document.Id")]
    public int SourceDocumentId { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;
}
