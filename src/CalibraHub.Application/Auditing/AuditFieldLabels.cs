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
            // Kalem alanları
            ["ItemId"] = "Malzeme",
            ["ItemCode"] = "Malzeme Kodu",
            ["ItemName"] = "Malzeme",
            ["Quantity"] = "Miktar",
            ["BaseQuantity"] = "Ana Birim Miktarı",
            ["Unit"] = "Birim",
            ["UnitId"] = "Birim",
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
}
