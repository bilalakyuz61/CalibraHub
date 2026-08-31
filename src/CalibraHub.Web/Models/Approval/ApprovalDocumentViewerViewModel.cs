namespace CalibraHub.Web.Models.Approval;

public sealed class ApprovalDocumentViewerViewModel
{
    public required int Id { get; init; }
    public required string DocumentNumber { get; init; }
    public required string Kind { get; init; }
    public required DateOnly IssueDate { get; init; }
    public required string SenderTaxNumber { get; init; }
    public string? SenderName { get; init; }
    public required string EnvelopeId { get; init; }
    public required string XmlContent { get; init; }

    /// <summary>
    /// "Geri" hedefi — belgenin TÜRÜNE ait liste ekranı.
    ///
    /// <para>Önceden <c>history.back()</c> kullanılıyordu; belge doğrudan bağlantıyla,
    /// yeni sekmede veya sayfa yenilendikten sonra açıldığında geçmiş boş olduğu için
    /// düğme ya hiçbir şey yapmıyor ya da uygulamanın dışına çıkıyordu.</para>
    /// </summary>
    public required string BackUrl { get; init; }

    /// <summary>
    /// Belgede gercek UBL XML var mi. OFFLINE (ERP) kayitlarda YOKTUR — o durumda
    /// "Resmi (GIB) Goruntusu" ve "XML" sekmeleri anlamsizdir, gosterilmez.
    /// </summary>
    public bool HasXmlPayload { get; init; }

    /// <summary>
    /// Belgenin ETTN'si (UBL <c>cbc:UUID</c>) — GİB tarafındaki tekil belge kimliği.
    ///
    /// <para>Ekranda ETTN gösterilir; <see cref="EnvelopeId"/> çevrimdışı kayıtlarda
    /// bizim ÜRETTİĞİMİZ dedup anahtarıdır (<c>NETSIS-EFAT-14154</c> gibi) ve kullanıcı
    /// için bir anlam taşımaz — "Zarf No" diye göstermek yanlış bilgi veriyordu.</para>
    /// </summary>
    public string? Ettn { get; init; }
    public InvoiceRenderData? RenderData { get; init; }
}
