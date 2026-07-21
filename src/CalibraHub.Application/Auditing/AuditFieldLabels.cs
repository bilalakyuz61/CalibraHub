namespace CalibraHub.Application.Auditing;

/// <summary>
/// Alan/aksiyon/entity kodlarının kullanıcıya gösterilecek Türkçe etiketleri.
/// Etiket log satırına yazılır (dosya kendi kendini açıklar); burada bulunamayan
/// alan adı olduğu gibi gösterilir.
///
/// Yeni bir ekran enstrümante ederken alan etiketi eksikse buraya ekle —
/// entity-spesifik çakışma varsa "{entity}.{Field}" anahtarıyla override edilebilir.
/// </summary>
public static class AuditFieldLabels
{
    /// <summary>Entity kodu → kullanıcı etiketi (merkezi ekran filtreleri de bunu kullanır).</summary>
    public static readonly IReadOnlyDictionary<string, string> EntityLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Belge tipleri (DocumentType.Code — SeedDocumentTypesAsync ile birebir)
            ["satis_teklifi"]     = "Satış Teklifi",
            ["satis_siparisi"]    = "Satış Siparişi",
            ["alis_talebi"]       = "İhtiyaç Kaydı",
            ["satin_alma_talebi"] = "Satın Alma Talebi",
            ["alis_teklifi"]      = "Satın Alma Teklifi",
            ["alis_siparisi"]     = "Satın Alma Siparişi",
            ["depo_giris"]        = "Ambar Giriş",
            ["depo_cikis"]        = "Ambar Çıkış",
            ["depo_transfer"]     = "Depo Transferi",
            ["sayim"]             = "Sayım Fişi",
            ["is_emri"]           = "İş Emri",
            ["arge_proje"]        = "AR-GE Projesi",
            ["fatura"]            = "Fatura",
            ["irsaliye"]          = "İrsaliye",
            ["satis_irsaliyesi"]  = "Satış İrsaliyesi",
            ["alis_irsaliyesi"]   = "Alış İrsaliyesi",
            // Sabit entity'ler
            ["Item"]           = "Malzeme Kartı",
            ["Contact"]        = "Cari Hesap",
            ["WorkOrder"]      = "İş Emri",
            ["Bom"]            = "Ürün Ağacı",
            ["Personnel"]      = "Personel",
            ["Machine"]        = "Makine",
            ["Department"]     = "Departman",
            ["User"]           = "Kullanıcı",
            ["Operation"]      = "Operasyon",
            ["Routing"]        = "Rota",
            ["Location"]       = "Lokasyon",
            ["PriceList"]      = "Fiyat Listesi",
            ["ApprovalFlow"]   = "Onay Akışı",
            ["Asset"]          = "Varlık",
            ["Session"]        = "Oturum",
        };

    /// <summary>Aksiyon kodu → Türkçe etiket.</summary>
    public static readonly IReadOnlyDictionary<string, string> ActionLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AuditActions.Insert]      = "Ekleme",
            [AuditActions.Update]      = "Güncelleme",
            [AuditActions.Delete]      = "Silme",
            [AuditActions.Login]       = "Giriş",
            [AuditActions.LoginFailed] = "Başarısız Giriş",
            [AuditActions.Logout]      = "Çıkış",
            [AuditActions.Event]       = "Olay",
        };

    /// <summary>
    /// Ortak alan adı → Türkçe etiket sözlüğü. Önce "{entity}.{Field}" tam anahtarına,
    /// sonra alan adına bakılır; ikisi de yoksa alan adı aynen döner.
    /// </summary>
    private static readonly Dictionary<string, string> FieldLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Ad",
            ["FullName"] = "Ad Soyad",
            ["Code"] = "Kod",
            ["Description"] = "Açıklama",
            ["Title"] = "Başlık",
            ["Status"] = "Durum",
            ["IsActive"] = "Aktif",
            ["Note"] = "Not",
            ["Notes"] = "Notlar",
            // Belge alanları
            ["DocumentNumber"] = "Belge No",
            ["DocumentDate"] = "Belge Tarihi",
            ["DueDate"] = "Termin Tarihi",
            ["ValidUntil"] = "Geçerlilik Tarihi",
            ["DeliveryDate"] = "Teslim Tarihi",
            ["ContactId"] = "Cari",
            ["ContactName"] = "Cari",
            ["ContactCode"] = "Cari Kodu",
            ["Currency"] = "Para Birimi",
            ["CurrencyCode"] = "Para Birimi",
            ["ExchangeRate"] = "Kur",
            ["SalesRepId"] = "Satış Temsilcisi",
            ["SalesRepName"] = "Satış Temsilcisi",
            ["PaymentTerm"] = "Ödeme Koşulu",
            ["SubTotal"] = "Ara Toplam",
            ["DiscountTotal"] = "İskonto Toplamı",
            ["TaxTotal"] = "KDV Toplamı",
            ["GrandTotal"] = "Genel Toplam",
            // 2026-07-21: SnapHeader (DocumentService) diff'inde gerçekten kullanılan ama
            // sözlükte eksik olan belge başlığı alanları — Views/Sales/DocumentEdit.cshtml
            // "Kosullar" sekmesindeki etiketlerle tutarlı. PaymentTerms NOT: entity gerçek
            // property adı çoğuldur ("PaymentTerms"); aşağıdaki eski "PaymentTerm" (tekil)
            // anahtarı hiçbir zaman eşleşmiyordu, bu yeni anahtar asıl kullanılan olan.
            ["PaymentTerms"] = "Ödeme Koşulu",
            ["DeliveryTerms"] = "Teslimat Koşulu",
            ["DeliveryAddress"] = "Teslimat Adresi",
            ["ContactAddress"] = "Cari Adresi",
            ["DeliveryDays"] = "Teslim Süresi (Gün)",
            ["CurrencyId"] = "Para Birimi",
            ["RequesterPersonnelId"] = "Talep Eden",
            ["RevisionNo"] = "Revizyon No",
            ["DocumentId"] = "Belge",
            // Kalem alanları
            ["ItemId"] = "Malzeme",
            ["ItemCode"] = "Malzeme Kodu",
            ["ItemName"] = "Malzeme",
            ["Quantity"] = "Miktar",
            ["BaseQuantity"] = "Ana Birim Miktarı",
            ["Unit"] = "Birim",
            ["UnitId"] = "Birim",
            ["UnitCode"] = "Birim",
            ["UnitPrice"] = "Birim Fiyat",
            ["Price"] = "Fiyat",
            ["Amount"] = "Tutar",
            ["DiscountRate"] = "İskonto %",
            ["DiscountAmount"] = "İskonto Tutarı",
            ["TaxRate"] = "KDV %",
            ["LineTotal"] = "Satır Toplamı",
            ["MovementType"] = "Hareket Tipi",
            ["LocationId"] = "Lokasyon",
            ["LocationName"] = "Lokasyon",
            ["SourceLocationId"] = "Kaynak Lokasyon",
            ["TargetLocationId"] = "Hedef Lokasyon",
            ["FromLocationId"] = "Çıkış Lokasyonu",
            ["ToLocationId"] = "Giriş Lokasyonu",
            ["LotId"] = "Lot",
            ["LotNumber"] = "Lot No",
            ["LotNo"] = "Lot No",
            ["SerialNumber"] = "Seri No",
            ["UnitCost"] = "Birim Maliyet",
            ["ArgeProjectId"] = "AR-GE Projesi",
            // Kart alanları
            ["Email"] = "E-posta",
            ["Phone"] = "Telefon",
            ["Phone2"] = "Telefon 2",
            ["Address"] = "Adres",
            ["City"] = "Şehir",
            ["District"] = "İlçe",
            ["Country"] = "Ülke",
            ["TaxOffice"] = "Vergi Dairesi",
            ["TaxNumber"] = "Vergi No",
            ["Iban"] = "IBAN",
            ["Website"] = "Web Sitesi",
            ["GroupId"] = "Grup",
            ["GroupName"] = "Grup",
            ["CategoryId"] = "Kategori",
            ["Barcode"] = "Barkod",
            ["MinStock"] = "Asgari Stok",
            ["MaxStock"] = "Azami Stok",
            ["IsMachinePark"] = "Makine Parkı",
            ["IsCountReference"] = "Sayım Referansı",
            ["IsSingleChildType"] = "Tek Tür Alt Kırılım",
            ["DepartmentId"] = "Departman",
            ["DepartmentName"] = "Departman",
            ["Role"] = "Rol",
            // Cari (Contact) alanları
            ["AccountCode"] = "Hesap Kodu",
            ["AccountTitle"] = "Ünvan",
            ["AccountType"] = "Hesap Tipi",
            ["IdentityNumber"] = "Kimlik No",
            ["Mobile"] = "Cep Telefonu",
            ["PostalCode"] = "Posta Kodu",
            ["Neighborhood"] = "Mahalle",
            ["CountryCode"] = "Ülke",
            ["ContactPerson"] = "İlgili Kişi",
            ["PriceGroupId"] = "Fiyat Grubu",
            ["SalesRepresentativeId"] = "Satış Temsilcisi",
            ["ContactGroupId"] = "Cari Grubu",
            ["WaPhone"] = "WhatsApp Telefonu",
            ["WaName"] = "WhatsApp Adı",
            // Malzeme kartı alanları
            ["TypeId"] = "Malzeme Tipi",
            ["Combinations"] = "Kombinasyon Özellikleri",
            ["TrackingType"] = "Takip Tipi",
            ["AutoSerial"] = "Otomatik Seri",
            // Tanımlar (Personel / Makine / Departman / Kullanıcı)
            ["HourlyCapacity"] = "Saatlik Kapasite",
            ["SortOrder"] = "Sıralama",
            ["IsProductionOperator"] = "Üretim Operatörü",
            ["CardNo"] = "Kart No",
            ["PinCode"] = "PIN",
            ["BirthDate"] = "Doğum Tarihi",
            ["Department"] = "Departman",
            ["ParentDepartmentId"] = "Üst Departman",
            ["EmployeeCode"] = "Sicil Kodu",
            ["SupervisorUserId"] = "Amir",
            ["PhoneNumber"] = "Telefon",
            ["CompanyId"] = "Şirket",
            ["UserId"] = "Bağlı Kullanıcı",
            ["Password"] = "Şifre",
            // İş emri / üretim
            ["WorkOrderNumber"] = "İş Emri No",
            // 2026-07-21: WorkOrderDto'nun gerçek property adları (WorkOrderService Insert
            // snapshot'ı) — "WorkOrderNumber" değil "OrderNumber"/"OrderDate" (WorkOrderEdit.cshtml
            // "Numara" / "İş Emri Tarihi" alanlarıyla aynı kavram, audit listesinde bağlamsız
            // görünmesin diye "İş Emri" öneki korunur).
            ["OrderNumber"] = "İş Emri No",
            ["OrderDate"] = "İş Emri Tarihi",
            ["PlannedQuantity"] = "Planlanan Miktar",
            ["CompletedQuantity"] = "Tamamlanan Miktar",
            ["PlannedStart"] = "Planlanan Başlangıç",
            ["PlannedEnd"] = "Planlanan Bitiş",
            ["PlannedStartDate"] = "Planlanan Başlangıç",
            ["PlannedEndDate"] = "Planlanan Bitiş",
            ["RoutingId"] = "Rota",
            ["MachineId"] = "Makine",
            ["DefaultMachineId"] = "Varsayılan Makine",
            ["OperationId"] = "Operasyon",
            ["ShiftId"] = "Vardiya",
            ["Priority"] = "Öncelik",
            ["AssignedUserId"] = "Atanan Kullanıcı",
            ["AssignedPersonnelId"] = "Atanan Personel",
            ["WarehouseLocationId"] = "Depo",
            ["ConfigId"] = "Kombinasyon",
            // ShopFloor operasyon/aktivite (2026-07-16 — audit enstrümantasyonu)
            ["ProducedQuantity"] = "Üretilen Miktar",
            ["ScrapQuantity"] = "Fire Miktarı",
            ["IssuedQuantity"] = "Sarf Edilen Miktar",
            ["ActivityType"] = "Aktivite Tipi",
            ["ActivityReasonId"] = "Aktivite Sebebi",
            ["StartedByPersonnelId"] = "Başlatan Personel",
            ["CompletedByPersonnelId"] = "Tamamlayan Personel",
            ["OperatorPersonnelId"] = "Operatör",
        };

    /// <summary>Alan etiketi çözümle — bulunamazsa alan adının kendisi döner.</summary>
    public static string Resolve(string? entity, string field)
    {
        if (!string.IsNullOrEmpty(entity) &&
            FieldLabels.TryGetValue(entity + "." + field, out var scoped))
            return scoped;
        return FieldLabels.TryGetValue(field, out var label) ? label : field;
    }

    /// <summary>Entity etiketi çözümle — bulunamazsa kod aynen döner.</summary>
    public static string EntityLabel(string? entity) =>
        entity is not null && EntityLabels.TryGetValue(entity, out var label) ? label : entity ?? "";

    // ── Değer (VALUE) çevirisi — 2026-07-21, PageComment Seq 20 geri bildirimi ──────────────
    //
    // Yalnız GÖRÜNTÜLEME katmanında uygulanır (bkz. ResolveChange / AuditLogController.ToDto).
    // Log dosyasına HAM enum/bool değeri yazılmaya devam eder (yazım tarafı değişmedi) — çeviri
    // her okumada burada yapıldığı için sözlük büyüdükçe ESKİ log kayıtları da otomatik düzelir.
    // Eşleşme yoksa ham değer AYNEN döner (muhafazakâr — serbest metne asla dokunulmaz).
    // Ordinal (case-sensitive, TryGetValue varsayılanı) karşılaştırma KASITLI: örn. PageComment.Status
    // ham değeri küçük harf "approved" yazar — DocumentStatus.Approved ("Approved") ile KARIŞMAMALI.

    /// <summary>DocumentType.Code kodlu belge entity'leri — Status alanı bu kodlarda DAİMA
    /// DocumentStatus enum'ından gelir (bkz. DocumentStatusValueLabels). Kaynak: SeedDocumentTypesAsync
    /// ile aynı liste (EntityLabels'ın belge-tipi bölümüyle birebir).</summary>
    private static readonly HashSet<string> DocumentEntityCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "satis_teklifi", "satis_siparisi", "alis_talebi", "satin_alma_talebi",
        "alis_teklifi", "alis_siparisi", "depo_giris", "depo_cikis", "depo_transfer",
        "sayim", "is_emri", "arge_proje", "fatura", "irsaliye",
        "satis_irsaliyesi", "alis_irsaliyesi",
    };

    /// <summary>DocumentStatus enum değerleri → Türkçe. Kaynak: Views/Sales/DocumentEdit.cshtml
    /// "Durum Degistir" alt-menüsü + sqLinStatus (ilişkili belgeler grafiği) — Draft/Approved/
    /// Cancelled/Converted birebir eşleşiyor; Sent enum'un kendi [Description]'ı ("Onay Bekliyor")
    /// ile aynı, aynı zamanda Views/Purchase/FulfillModal.cshtml'de de kullanılıyor.</summary>
    private static readonly Dictionary<string, string> DocumentStatusValueLabels =
        new(StringComparer.Ordinal)
        {
            ["Draft"] = "Taslak",
            ["Sent"] = "Onay Bekliyor",
            ["Approved"] = "Onaylandı",
            ["Rejected"] = "Reddedildi",
            ["Revised"] = "Revize",
            ["Cancelled"] = "İptal",
            ["Converted"] = "Dönüştürüldü",
        };

    /// <summary>WorkOrderStatus ∪ WorkOrderOperationStatus — entity="WorkOrder" altında iki
    /// farklı enum aynı "Status" alan adını paylaşır (üst seviye emir durumu vs. "Operation[n].Status"
    /// bileşik alanı). Token'lar arasında anlam çakışması YOK (InProgress/Completed/Cancelled her
    /// ikisinde de aynı anlama gelir) → tek sözlükte güvenle birleştirilebilir. Kaynak:
    /// ProductionController.WorkOrderStatusLabel + Views/Production/ShopFloor.cshtml statusLabel.</summary>
    private static readonly Dictionary<string, string> WorkOrderStatusValueLabels =
        new(StringComparer.Ordinal)
        {
            ["Planned"] = "Taslak",
            ["Released"] = "Yayımlandı",
            ["InProgress"] = "Devam ediyor",
            ["Completed"] = "Tamamlandı",
            ["Closed"] = "Kapatıldı",
            ["Cancelled"] = "İptal",
            ["Pending"] = "Bekliyor",
            ["Skipped"] = "Atlandı",
        };

    /// <summary>WorkOrderActivityType (ShopFloor aktivite tipi). Kaynak: Domain/Enums/
    /// WorkOrderActivityType.cs [Description] öznitelikleri (ProductionController.GetActivityTypeLabel
    /// bunları reflection ile okur — burada aynı metinler sabit sözlük olarak tutulur).</summary>
    private static readonly Dictionary<string, string> ActivityTypeValueLabels =
        new(StringComparer.Ordinal)
        {
            ["Setup"] = "Hazırlık",
            ["Production"] = "Üretim",
            ["MaterialWait"] = "Malzeme Bekleme",
            ["MachineBreakdown"] = "Arıza",
            ["QualityCheck"] = "Kalite Kontrol",
            ["Break"] = "Mola",
            ["ShiftChange"] = "Vardiya Değişimi",
            ["PlannedDowntime"] = "Planlı Durma",
            ["Other"] = "Diğer",
        };

    /// <summary>WorkOrderPriority — tek Priority enum'u sistemde (kod-genelinde alan adı çakışması
    /// yok), entity'den bağımsız uygulanır. Kaynak: ProductionController.WorkOrderPriorityLabel.</summary>
    private static readonly Dictionary<string, string> PriorityValueLabels =
        new(StringComparer.Ordinal)
        {
            ["Low"] = "Düşük",
            ["Medium"] = "Normal",
            ["High"] = "Yüksek",
        };

    /// <summary>Item.TrackingType — üç sabit değer, entity'den bağımsız uygulanır. Kaynak:
    /// Domain/Entities/Item.cs XML doc: "None" (Yok) | "Lot" (Lot takibi) | "Serial" (Seri takibi).</summary>
    private static readonly Dictionary<string, string> TrackingTypeValueLabels =
        new(StringComparer.Ordinal)
        {
            ["None"] = "Yok",
            ["Lot"] = "Lot",
            ["Serial"] = "Seri",
        };

    /// <summary>DocumentLine.FulfillmentStatus (int). Bugün bu alan HAM diff'lenmiyor —
    /// DocumentService.LogFulfillmentAuditAsync zaten kendi formatlanmış metnini üretiyor
    /// ("25 (Karşılandı)"), bkz. DocumentService.FulfillmentStatusLabel (aynı değerler, tek kaynak
    /// burada tekrarlanır). İleride genel bir diff bu alanı ham sayı olarak sızdırırsa yine de
    /// doğru çevrilsin diye burada da tutulur (savunma amaçlı).</summary>
    private static readonly Dictionary<string, string> FulfillmentStatusValueLabels =
        new(StringComparer.Ordinal)
        {
            ["0"] = "Açık",
            ["1"] = "Kısmen Karşılandı",
            ["2"] = "Karşılandı",
            ["3"] = "Kapatıldı",
        };

    /// <summary>DocumentLine.MovementType (byte?, StockMovementType enum). Bugün ham diff'lenmiyor
    /// (BuildLineChanges/BuildStockLineChanges bu alanı işlemiyor); sayısal (AuditDiff.Normalize'ın
    /// byte için ürettiği "1".."4") ve enum-adı iki biçim de kapsanır (savunma amaçlı). Kaynak:
    /// Domain/Enums/StockMovementType.cs.</summary>
    private static readonly Dictionary<string, string> MovementTypeValueLabels =
        new(StringComparer.Ordinal)
        {
            ["1"] = "Çıkış", ["Issue"] = "Çıkış",
            ["2"] = "Giriş", ["Receipt"] = "Giriş",
            ["3"] = "Transfer", ["Transfer"] = "Transfer",
            ["4"] = "Düzeltme", ["Adjust"] = "Düzeltme",
        };

    /// <summary>
    /// Bileşik alan adında ("Operation[5].Status", "Activity[9].ActivityType", "Line[12].Quantity")
    /// son "." sonrası baz alan adını döner; düz alan adlarında ("Status") değişiklik yapmaz.
    /// </summary>
    private static string ExtractBaseField(string field)
    {
        var dot = field.LastIndexOf('.');
        return dot >= 0 && dot < field.Length - 1 ? field[(dot + 1)..] : field;
    }

    /// <summary>
    /// Bilinen enum-benzeri alanların ham değerini Türkçe görünen ada çevirir — alan bazlı ve
    /// muhafazakârdır (yalnız aşağıda listelenen alan adları için harita denenir; eşleşme yoksa
    /// veya alan haritada yoksa ham değer AYNEN döner). "Status" alanı entity'ye göre iki farklı
    /// enum'a ayrılır (Document belge tipleri vs. WorkOrder); diğerleri (Priority/TrackingType/
    /// FulfillmentStatus/MovementType) tek anlamlı olduğu için entity'den bağımsız uygulanır.
    /// Son adım: AuditDiff.Normalize TÜM bool alanları tam olarak "true"/"false" yazar (tip-güvenli;
    /// bir string alan asla bu iki literali birebir üretmez) → alan adından bağımsız güvenle çevrilir.
    /// </summary>
    public static string? ResolveValue(string? entity, string field, string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        switch (ExtractBaseField(field))
        {
            case "Status":
                if (!string.IsNullOrEmpty(entity) && DocumentEntityCodes.Contains(entity) &&
                    DocumentStatusValueLabels.TryGetValue(raw, out var docStatus))
                    return docStatus;
                if (string.Equals(entity, "WorkOrder", StringComparison.OrdinalIgnoreCase) &&
                    WorkOrderStatusValueLabels.TryGetValue(raw, out var woStatus))
                    return woStatus;
                break;
            case "ActivityType":
                if (string.Equals(entity, "WorkOrder", StringComparison.OrdinalIgnoreCase) &&
                    ActivityTypeValueLabels.TryGetValue(raw, out var actType))
                    return actType;
                break;
            case "Priority":
                if (PriorityValueLabels.TryGetValue(raw, out var priority)) return priority;
                break;
            case "TrackingType":
                if (TrackingTypeValueLabels.TryGetValue(raw, out var tracking)) return tracking;
                break;
            case "FulfillmentStatus":
                if (FulfillmentStatusValueLabels.TryGetValue(raw, out var fulfillment)) return fulfillment;
                break;
            case "MovementType":
                if (MovementTypeValueLabels.TryGetValue(raw, out var movement)) return movement;
                break;
        }

        if (raw == "true") return "Evet";
        if (raw == "false") return "Hayır";

        return raw;
    }

    /// <summary>
    /// Görüntüleme için tek bir alan değişikliğini (etiket + eski + yeni) çözümler — Search/Record/
    /// Excel export'un TEK ortak giriş noktası (bkz. AuditLogController.ToDto). Widget (Ek Alanlar)
    /// birleştirilmiş girdilerine ("Src"="Widget") DOKUNULMAZ: WidgetTraLog geçmişi zaten kendi
    /// Label'ını taşır ve serbest widget kodu (Field) tesadüfen bilinen bir sistem alan adına denk
    /// gelebilir (ör. admin "Status" adlı özel bir alan tanımlamış olabilir) — bu durumda yanlış
    /// çeviri riski oluşur, o yüzden widget girdileri baştan hariç tutulur.
    /// Etiket geriye dönük düzeltme: eski log satırında Label hâlâ Field'a birebir eşitse (yazıldığı
    /// anda sözlükte karşılığı yoktu) sözlük şimdi büyüdüyse yeniden çözümlenir; Label zaten Field'dan
    /// farklıysa (bilinçli özel etiket, ör. "Kalem Silindi — X") DOKUNULMAZ.
    /// </summary>
    public static (string Label, string? Old, string? New) ResolveChange(
        string? entity, string? src, AuditFieldChange change)
    {
        if (string.Equals(src, "Widget", StringComparison.Ordinal))
            return (change.Label ?? change.Field, change.Old, change.New);

        var label = string.Equals(change.Label, change.Field, StringComparison.Ordinal)
            ? Resolve(entity, change.Field)
            : (change.Label ?? change.Field);

        return (label, ResolveValue(entity, change.Field, change.Old), ResolveValue(entity, change.Field, change.New));
    }
}
