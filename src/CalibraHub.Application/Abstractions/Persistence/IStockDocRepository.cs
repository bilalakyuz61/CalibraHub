using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IStockDocRepository
{
    Task<IReadOnlyList<StockDocDto>> GetByTypeAsync(string docType, CancellationToken ct);
    Task<IReadOnlyList<StockDocDto>> GetByTypesAsync(IEnumerable<string> docTypes, CancellationToken ct);
    Task<StockDocDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<StockDocLineDto>> GetLinesAsync(int docId, CancellationToken ct);
    Task<(int Id, string DocNo)> SaveAsync(SaveStockDocRequest request, int? createdById, CancellationToken ct);

    /// <summary>Bir siparişin açık (teslim edilmemiş) kalemlerini kısmi teslimat modalı için döner.
    /// Miktarlar gösterim biriminde (Quantity birimi). BaseQuantity &gt; DeliveredQuantity olan satırlar.</summary>
    Task<IReadOnlyList<OrderOpenLineDto>> GetOrderOpenLinesAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Satış siparişi → Satış İrsaliyesi (stok etkili teslimat): siparişin açık (teslim edilmemiş)
    /// kalemleri için tek transaction'da satis_irsaliyesi belgesi + ana birimde çıkış satırları
    /// (MovementType=1, FromLocationId) yazar, sipariş satırlarının DeliveredQuantity'sini artırır
    /// ve eksi bakiye kontrolünü uygular. Yetersiz stokta NegativeBalanceException fırlatır (tx geri
    /// alınır). İrsaliye siparişe ParentDocumentId + kalem SourceLineId ile bağlanır; başlık tutarları
    /// teslim edilen kalemlerden yeniden hesaplanır (kısmi teslimatta orantılı).
    /// <paramref name="deliverByLine"/> = LineId → teslim miktarı (gösterim birimi); null ise TÜM açık
    /// miktar teslim edilir (geriye uyum). Miktar açık miktarı aşamaz; ≤0 olan satır atlanır.
    /// </summary>
    Task<(int Id, string DocNo)> DeliverSalesOrderAsync(
        int salesOrderId, int? createdById, IReadOnlyDictionary<int, decimal>? deliverByLine, CancellationToken ct);

    /// <summary>
    /// Satın alma siparişi → Alış İrsaliyesi (stok etkili mal kabul): açık sipariş kalemleri için
    /// alis_irsaliyesi belgesi + ana birimde giriş satırları (MovementType=2, LocationId) yazar,
    /// sipariş satırlarının DeliveredQuantity'sini artırır. Giriş bakiyeyi artırdığı için eksi bakiye
    /// kontrolü uygulanmaz. İrsaliye siparişe ParentDocumentId + kalem SourceLineId ile bağlanır.
    /// <paramref name="deliverByLine"/> = LineId → kabul miktarı (gösterim birimi); null ise TÜM açık
    /// miktar kabul edilir. Miktar açık miktarı aşamaz; ≤0 olan satır atlanır.
    /// </summary>
    Task<(int Id, string DocNo)> ReceivePurchaseOrderAsync(
        int purchaseOrderId, int? createdById, IReadOnlyDictionary<int, decimal>? deliverByLine, CancellationToken ct);

    /// <summary>
    /// Sipariş seri rezervasyonu (2026-07-11): sipariş satırlarına seçilen serileri DocumentLineSerial
    /// ile bağlar. reserve=true (ORDER_SERIAL_RESERVATION + stok rez. açık) ise InStock→Reserved(4) +
    /// ReservedForDocumentId. "Reset+rebuild": her kayıtta belgenin bağları/rezervasyonları sıfırlanıp
    /// payload'dan yeniden kurulur (orphan/diff bug yok). Seri stokta yoksa / başka siparişte rezerve
    /// ise hata döner (Ok=false, tx geri alınır).
    /// </summary>
    Task<(bool Ok, string? Error)> ReconcileOrderSerialsAsync(
        int documentId,
        IReadOnlyList<(int LineId, int ItemId, IReadOnlyList<string> Serials)> lineSerials,
        bool reserve, CancellationToken ct);

    /// <summary>Sipariş iptal/silmede rezerve serileri serbest bırakır (Reserved→InStock). Bağlar iz olarak kalır.</summary>
    Task ReleaseOrderSerialReservationsAsync(int documentId, CancellationToken ct);

    /// <summary>
    /// Mobil FIFO teslimat (2026-07-16): cari + malzeme miktarlarından tek transaction'da stok etkili
    /// irsaliye (satis_irsaliyesi / alis_irsaliyesi) yazar. <paramref name="fifoEnabled"/> açıksa her
    /// malzemenin miktarı carinin AÇIK sipariş satırlarına (aynı malzeme, kalan > 0) belge tarihi/numarası
    /// ARTAN sırada tahsis edilir; bir malzeme birden çok siparişe bölünebilir (sipariş satırı başına ayrı
    /// irsaliye satırı). Kaynak bağı web kısmi-teslimatıyla BİREBİR AYNI: irsaliye satırı SourceLineId +
    /// sipariş satırı DeliveredQuantity. Artan (bağlanamayan) miktar bağlantısız satır olur (fiyat =
    /// <see cref="MobileDeliveryLineInput.FallbackUnitPrice"/>, depo = malzemenin varsayılan deposu;
    /// varsayılan depo yoksa InvalidOperationException). <paramref name="forbidUnlinked"/> açıksa herhangi
    /// bir malzemede bağlantısız miktar kalırsa hiçbir şey yazılmadan InvalidOperationException fırlatılır.
    /// Satış (çıkış, MovementType=1) NegativeBalanceGuard uygular; alış (giriş, MovementType=2) uygulamaz.
    /// Numara ResolveDocNoByCode ile üretilir. Miktarlar ana birimdedir (BaseQuantity).
    ///
    /// LOT + SERİ (2026-07-16): Lot/seri-takipli malzeme artık delivery yolunda REDDEDİLMEZ; web ambar
    /// akışıyla (SaveDirectDocAsync) BİREBİR aynı guard'lar uygulanır (TrackingType DB'den çözülür).
    ///   • ALIŞ (giriş): lot yoksa oluşturulur; seriler girilir ya da AutoSerial kartında
    ///     <see cref="MobileDeliveryLineInput.AutoGenerateSerials"/> ile üretilir (ResolveSerialsForLine giriş semantiği).
    ///   • SATIŞ (çıkış): lot mevcut olmalı (+ lot bakiye guard'ı); seri öncelik zinciri —
    ///     (1) bağlanan sipariş satırına REZERVE seri varsa o kullanılır; (2)
    ///     <paramref name="serialOverrideEnabled"/> AÇIKKEN istemci serileri rezerveyi override eder,
    ///     KAPALIYKEN farklı seri gönderilirse tüm kayıt reddedilir; (3) rezerve yok + istemci seçmezse
    ///     FIFO otomatik (en eski müsait InStock seriler). Seri adedi = teslim adedi (tam sayı) zorunlu.
    ///
    /// FAZ C (2026-07-17):
    ///   • <paramref name="preferredOrderId"/> — verilmişse her malzeme için FIFO taraması bu siparişin
    ///     açık satırlarını ÖNCE tüketir (sort öncelik), artan miktar normal FIFO'ya (tüm açık siparişler,
    ///     tarih artan) düşer. <paramref name="fifoEnabled"/>=false + preferredOrderId verilmişse yalnız bu
    ///     siparişin açık satırları taranır (tüm carinin siparişleri DEĞİL) — artan miktar bağlantısız kalır.
    ///     Cari/belge-türü uyuşmuyorsa (sorgu onu bulamaz) sessizce etkisizdir, normal davranışa düşülür.
    ///   • <paramref name="externalRefNumber"/> — Tedarikçi İrsaliye No, Document.ExternalRefNumber'a
    ///     olduğu gibi yazılır (trim/cap zaten controller'da yapılır, repo aynen INSERT eder).
    /// </summary>
    Task<MobileDeliveryResult> SaveDeliveryFifoAsync(
        bool isPurchase, int contactId, string? note,
        IReadOnlyList<MobileDeliveryLineInput> lines,
        bool fifoEnabled, bool forbidUnlinked, bool serialOverrideEnabled,
        int? preferredOrderId, string? externalRefNumber,
        int? createdById, CancellationToken ct);

    /// <summary>
    /// Üretim sarfı (iş emri bileşen sarfı, 2026-07-10) — tek transaction'da: iş emrinin
    /// belgesine MovementType=1 (çıkış) satırları yazar; lot/seri kurallarını stok çıkışıyla
    /// birebir uygular (lot-takipli → mevcut lot zorunlu, seri-takipli → stoktaki InStock
    /// serilerden adet kadar seçim → Issued); WorkOrderComponent.IssuedQuantity'yi artırır
    /// (serbest satırda RequiredQuantity=0 "Serbest sarf" bileşen kaydı açılır) ve eksi
    /// bakiye + lot bakiye kontrollerini çalıştırır. Yazılan satır sayısını döner.
    /// </summary>
    Task<int> IssueWorkOrderConsumptionAsync(WorkOrderConsumptionRequest request, int? createdById, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);
}
