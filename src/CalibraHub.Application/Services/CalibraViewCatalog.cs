namespace CalibraHub.Application.Services;

/// <summary>
/// DB Schema haritasinda gorunen, CalibraHub tarafindan olusturulan/kullanilan
/// SQL view'larinin belgelenme katalogu.
///
/// YENİ VIEW EKLENDIGINDE: Buraya giris ekle. IsCustomizable = true olan view'lar
/// kullanicinin SQL'ini degistirebildigi, yani uygulama davranisini etkileyebildigi
/// view'lardir. Ekran yolu UsedIn alaninda belirtilir.
/// </summary>
public static class CalibraViewCatalog
{
    public static readonly IReadOnlyList<CalibraViewEntry> Entries =
    [
        // ── Özelleştirilebilir view'lar ────────────────────────────────────────
        new("cbv_FulfillmentLineExtras",
            "Karşılama Merkezi ek kolon genişletmesi — bu view'a SELECT ettiğiniz her kolon, Karşılama Merkezi ekranında kalem satırlarına otomatik eklenir. " +
            "Zorunlu sütunlar: DocumentId ve LineId. Kolonu eklemek için SELECT listesini genişletin; uygulama restart gerekmez.",
            "Satın Alma → Karşılama Merkezi (/Purchase/FulfillmentCenter)",
            IsCustomizable: true),

        // ── Rehber (cbv_Guide_*) view'ları ────────────────────────────────────
        new("cbv_Guide_Contacts",
            "Cari hesap rehber araması — kod ve ad eşleştirme. Belge formlarındaki cari seçici bu view üzerinden çalışır.",
            "Belge formları (Teklif, Sipariş, Fatura) — cari seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Suppliers",
            "Tedarikçi (cari) rehber araması. Satın alma formlarındaki tedarikçi seçici bu view'ı kullanır.",
            "Satın alma formları — tedarikçi seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Items",
            "Malzeme kartı rehber araması — tüm malzeme tiplerine açık.",
            "Belge kalem satırları, BOM, İş emri — malzeme seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Items_Finished",
            "Mamul malzeme filtrelenmiş rehber — yalnızca üretim/mamul tipindeki malzemeler gösterilir.",
            "Üretim iş emri oluşturma — mamul seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Routing",
            "Rota (üretim reçetesi) rehber araması.",
            "İş emri formları — rota seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Personnel",
            "Personel rehber araması.",
            "Operasyon / üretim formları — personel seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Machines",
            "Makine rehber araması.",
            "Operasyon / üretim formları — makine seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Locations",
            "Lokasyon (depo / raf) rehber araması.",
            "Stok hareketi, irsaliye formları — lokasyon seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_WorkOrders",
            "İş emri rehber araması.",
            "Belge formları — iş emri seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_Documents",
            "Satış teklifi ve sipariş belge rehber araması.",
            "Belge formları — kaynak belge seçici widget",
            IsCustomizable: false),

        new("cbv_Guide_SalesOrders",
            "Satış siparişi filtrelenmiş rehber araması.",
            "Karşılama / sipariş formları — sipariş seçici widget",
            IsCustomizable: false),

        // ── Rapor motoru view'ları (vw_*) ─────────────────────────────────────
        new("vw_ReportDocument",
            "Rapor motoru belge kaynağı — tüm belge tiplerini birleştiren geniş view. " +
            "Rapor Tasarımcısında belge tabanlı raporlar bu view'ı veri kaynağı olarak kullanır.",
            "Rapor Tasarımcısı (/Report/Designs) — belge veri kaynağı",
            IsCustomizable: false),

        new("vw_DocumentCombination",
            "Belge kombinasyon detayları — konfigürasyon / özellik seçimi içeren belge kalemlerinin genişletilmiş görünümü.",
            "Rapor Tasarımcısı — kombinasyon veri kaynağı",
            IsCustomizable: false),

        new("vw_AssetAssignment",
            "Varlık atama geçmişi — bir varlığa yapılmış tüm atama kayıtlarının düzleştirilmiş listesi.",
            "Varlık Yönetimi (/Asset) — atama listesi ekranı",
            IsCustomizable: false),

        // ── DocLayout baskı şablonu view'ları ─────────────────────────────────
        new("vw_Invoice",
            "Fatura baskı şablonu veri kaynağı. DocLayout rapor tasarımcısında Fatura şablonları bu view üzerinden veri çeker.",
            "Tasarım (DocLayout) — Fatura baskı şablonu",
            IsCustomizable: false),

        new("vw_DeliveryNote",
            "İrsaliye baskı şablonu veri kaynağı. DocLayout rapor tasarımcısında İrsaliye şablonları bu view üzerinden veri çeker.",
            "Tasarım (DocLayout) — İrsaliye baskı şablonu",
            IsCustomizable: false),

        new("vw_ProductBarcode",
            "Ürün barkod baskı şablonu veri kaynağı.",
            "Tasarım (DocLayout) — Ürün barkod baskı şablonu",
            IsCustomizable: false),

        new("vw_ShelfLabel",
            "Raf etiketi baskı şablonu veri kaynağı.",
            "Tasarım (DocLayout) — Raf etiketi baskı şablonu",
            IsCustomizable: false),

        new("vw_Document",
            "Genel belge baskı şablonu veri kaynağı — tüm belge tipleri için evrensel görünüm.",
            "Tasarım (DocLayout) — Genel belge baskı şablonu",
            IsCustomizable: false),

        new("vw_MaterialCards",
            "Malzeme kartı baskı şablonu veri kaynağı.",
            "Tasarım (DocLayout) — Malzeme kartı baskı şablonu",
            IsCustomizable: false),
    ];

    private static readonly IReadOnlyDictionary<string, CalibraViewEntry> ByName =
        Entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

    public static CalibraViewEntry? FindByName(string name)
        => ByName.TryGetValue(name, out var entry) ? entry : null;
}

public sealed record CalibraViewEntry(
    string Name,
    string Description,
    string UsedIn,
    bool IsCustomizable);
