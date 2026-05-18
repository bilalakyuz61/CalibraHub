using CalibraHub.Web.Models.Logistics;

namespace CalibraHub.Web.Models.Sales;

public sealed class DocumentsViewModel
{
    public IReadOnlyCollection<GridColumnDefinition> AvailableColumns { get; init; } = [];
    public IReadOnlyCollection<string> VisibleColumns { get; init; } = [];

    // Server-side hazirlanan SmartBoard config'i. View inline JSON olarak
    // window.__CALIBRA_BOARD_CONFIG__'a gomer ve mountSmartBoard'a iletir.
    public object? BoardConfig { get; init; }
}

public sealed class DocumentEditViewModel
{
    public int? DocumentId { get; set; }

    /// <summary>
    /// CalibraLineItemsGrid icin server-side JSON config (pre-serialized).
    /// Kolonlar (columns) + bos satirlar (rows) — initial load.
    /// View bunu inline olarak window.__CALIBRA_SALES_QUOTE_LINE_GRID__'e yazar.
    /// </summary>
    public string LineGridConfigJson { get; set; } = "null";

    /// <summary>
    /// Belge tipi kodu (satis_teklifi / satis_siparisi). View baslik + SaveDocument
    /// body'sine documentTypeId injection icin kullanilir. Yeni belgede ?type=order
    /// parametresi ile geliyorsa siparis modu; mevcut belgede DB'den okunan deger.
    /// </summary>
    public string DocumentTypeCode { get; set; } = "satis_teklifi";

    /// <summary>Belge tipi DB ID'si — SaveDocument body'sinde gonderilir.</summary>
    public int? DocumentTypeId { get; set; }

    // ── Belge Tipi Metadata Hub (Faz N) — DocumentType tablosundan okunur ─────
    // View tarafı hard-code kontrol yapmaz; her şey bu alanlardan beslenir.
    // Yeni belge tipi (alış irsaliyesi, satınalma siparişi vb.) eklenince DB seed'i
    // değişmesi yeterli — view kodu dokunulmaz.

    /// <summary>Liste sayfası URL'i — save/delete sonrası geri dönüş hedefi.</summary>
    public string ListReturnUrl { get; set; } = "/Sales/Documents";

    /// <summary>Yeni kayıt URL'i (header dropdown / floating "Yeni" butonu için).</summary>
    public string? NewUrl { get; set; }

    /// <summary>Kullanıcıya gösterilen belge tipi adı (örn. "Satış Siparişi").</summary>
    public string DocumentTypeName { get; set; } = "Belge";

    /// <summary>Lucide icon adı (header rozeti için, örn. "ShoppingCart").</summary>
    public string? DocumentTypeIcon { get; set; }

    /// <summary>Tema renk anahtarı (emerald/indigo/amber...).</summary>
    public string? DocumentTypeIconColor { get; set; }

    /// <summary>Bu belge tipi entegrasyonla aktarılabilir mi (UI'da "ERP'ye Aktar" butonu için)?</summary>
    public bool IsTransferable { get; set; }
}
