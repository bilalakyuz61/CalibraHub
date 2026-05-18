using System.Text;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Services;

namespace CalibraHub.Web.Services;

/// <summary>
/// IEndpointCatalogService implementasyonu — Resources/Integration/*.csv
/// dosyalarini startup'ta okur, in-memory cache'ler.
///
/// Singleton — uygulama omru boyunca yasar (CSV degisirse restart gerek).
/// Lazy load: ilk Get cagrisinda parse edilir.
///
/// CSV format (Netsis): "Resource","Method","HttpMethod","UrlTemplate","InputType","ReturnType"
///
/// Kategori cikarimi: Resource adina gore heuristic
///   ItemSlips/Sales/Demand   → Sales
///   Purchase/Receipt          → Purchase
///   ARP/Customer/Cari         → Customer
///   Item/Stock/Warehouse/BOM  → Stock
///   Bank/CheckPNote/FastPay   → Bank
///   EDocument/EInvoice        → EDocument
///   diger                     → Other
/// </summary>
public sealed class EndpointCatalogService : IEndpointCatalogService
{
    private readonly IWebHostEnvironment _env;
    private readonly Lazy<IReadOnlyList<EndpointCatalogItem>> _items;

    public EndpointCatalogService(IWebHostEnvironment env)
    {
        _env = env;
        _items = new Lazy<IReadOnlyList<EndpointCatalogItem>>(LoadAll);
    }

    public IReadOnlyList<EndpointCatalogItem> GetAll() => _items.Value;

    public IReadOnlyList<EndpointCatalogItem> GetByProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "all", StringComparison.OrdinalIgnoreCase))
            return _items.Value;
        return _items.Value
            .Where(i => string.Equals(i.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ── Loading ───────────────────────────────────────────────────────────

    private IReadOnlyList<EndpointCatalogItem> LoadAll()
    {
        var dir = Path.Combine(_env.ContentRootPath, "Resources", "Integration");
        if (!Directory.Exists(dir))
        {
            Console.WriteLine($"[EndpointCatalog] Klasor yok: {dir}");
            return Array.Empty<EndpointCatalogItem>();
        }

        var all = new List<EndpointCatalogItem>();

        // Netsis CSV
        var netsisPath = Path.Combine(dir, "NetsisRestEndpoints.csv");
        if (File.Exists(netsisPath))
        {
            try
            {
                var rows = ParseCsv(File.ReadAllText(netsisPath, Encoding.UTF8));
                foreach (var row in rows)
                {
                    if (row.Count < 4) continue;
                    var resource    = row[0]?.Trim() ?? "";
                    var methodName  = row[1]?.Trim() ?? "";
                    var httpMethod  = (row[2]?.Trim() ?? "POST").ToUpperInvariant();
                    var urlTemplate = row[3]?.Trim() ?? "";
                    var inputType   = row.Count > 4 ? row[4]?.Trim() : null;
                    var returnType  = row.Count > 5 ? row[5]?.Trim() : null;

                    if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(urlTemplate))
                        continue;

                    all.Add(new EndpointCatalogItem(
                        Provider:    "Netsis",
                        Resource:    resource,
                        MethodName:  methodName,
                        HttpMethod:  httpMethod,
                        UrlTemplate: urlTemplate,
                        InputType:   string.IsNullOrEmpty(inputType)  ? null : inputType,
                        ReturnType:  string.IsNullOrEmpty(returnType) ? null : returnType,
                        Category:    GuessCategory(resource),
                        Summary:     BuildSummary(methodName)));
                }
                Console.WriteLine($"[EndpointCatalog] Netsis: {all.Count} endpoint yuklendi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EndpointCatalog] Netsis CSV parse hatasi: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"[EndpointCatalog] CSV bulunamadi: {netsisPath}");
        }

        return all;
    }

    // ── Summary ───────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> SummaryMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // CRUD standart
        ["GetInternal"]           = "Liste Getir",
        ["GetInternalById"]       = "ID ile Getir",
        ["GetInternalByParam"]    = "Parametreyle Getir",
        ["PostInternal"]          = "Yeni Kayıt Oluştur",
        ["PutInternal"]           = "Kayıt Güncelle",
        ["DeleteInternalById"]    = "ID ile Sil",
        ["DeleteInternalByParam"] = "Parametreyle Sil",
        ["InsertUpdate"]          = "Ekle / Güncelle",
        ["Describe"]              = "Alan Şeması",
        ["Get"]                   = "Veri Getir",
        ["Save"]                  = "Kaydet",
        ["Remove"]                = "Sil",
        ["Calculate"]             = "Hesapla",
        ["ReadAndSave"]           = "Oku ve Kaydet",
        // Auth / Sistem
        ["Login"]                 = "Oturum Aç",
        ["LogOut"]                = "Oturumu Kapat",
        ["RefreshToken"]          = "Token Yenile",
        ["Ping"]                  = "Bağlantı Testi",
        ["Version"]               = "Sürüm Bilgisi",
        ["NetsisMasterVersion"]   = "Ana Sürüm",
        ["NetsisMinorVersion"]    = "Alt Sürüm",
        ["GetLicInfo"]            = "Lisans Bilgisi",
        ["CompanyList"]           = "Şirket Listesi",
        ["IsCached"]              = "Önbellek Durumu",
        ["GetResult"]             = "Sonuç Getir",
        ["HasSpecialParameter"]   = "Özel Parametre Var mı",
        // İşlem / Transaction
        ["BeginTransaction"]      = "İşlem Başlat",
        ["CommitTransaction"]     = "İşlemi Onayla",
        ["RoolBackTransaction"]   = "İşlemi Geri Al",
        ["Clear"]                 = "Temizle",
        // Numara
        ["NewNumber"]             = "Yeni Numara",
        ["ReadLastNumber"]        = "Son Numarayı Oku",
        ["IncrementNumber"]       = "Sıra No Arttır",
        ["NewEArchiveNumber"]     = "e-Arşiv Numarası",
        ["NewEItemSlipsNumber"]   = "e-Stok Fişi Numarası",
        ["NewEWaybillNumber"]     = "e-İrsaliye Numarası",
        ["ChangeNumber"]          = "Numara Değiştir",
        ["ChangeCode"]            = "Kod Değiştir",
        ["ChangeArpsCode"]        = "Cari Kodu Değiştir",
        ["StockCodeChange"]       = "Stok Kodu Değiştir",
        ["ChangeItemSlipNumber"]  = "Stok Fişi No Değiştir",
        ["ChangeProducerNumber"]  = "Üretici No Değiştir",
        // Bakiye / Finans
        ["BringBalance"]          = "Bakiye Getir",
        ["LastBalance"]           = "Son Bakiye",
        ["BalanceAllOrders"]      = "Tüm Siparişleri Dengele",
        ["BalanceAllProductionOrders"] = "Üretim Emirlerini Dengele",
        ["BalanceAllRequisitions"]= "Talepleri Dengele",
        ["ArpsBalanceExchangeDiff"] = "Cari Kur Farkı",
        ["ArpsMovementControl"]   = "Cari Hareket Kontrolü",
        ["CustomerRisk"]          = "Müşteri Riski",
        ["AuthorizedBranches"]    = "Yetkili Şubeler",
        // Sipariş / Satış
        ["ConvertToOrder"]        = "Siparişe Dönüştür",
        ["ConvertToOffer"]        = "Teklife Dönüştür",
        ["OrderRevision"]         = "Sipariş Revizyonu",
        ["ContractsOpenPrice"]    = "Sözleşme Açık Fiyat",
        ["SiparisToIrsFat"]       = "Sipariş → İrsaliye/Fatura",
        ["TopluSiparisToIrsFat"]  = "Toplu Sipariş → İrsaliye/Fatura",
        ["IrsToFat"]              = "İrsaliye → Fatura",
        // e-Belge
        ["SendEDocument"]         = "e-Belge Gönder",
        ["CheckEDocument"]        = "e-Belge Kontrol",
        ["ShowEDocument"]         = "e-Belgeyi Görüntüle",
        ["EDocumentResponse"]     = "e-Belge Yanıtı",
        ["EDocumentItemResponse"] = "e-Belge Kalem Yanıtı",
        ["EDocCurrentUpdate"]     = "e-Belge Güncelle",
        ["EArchiveCancellationInvoice"] = "e-Arşiv İptal Faturası",
        // Yazdırma
        ["GLSlipsPrinting"]       = "Fiş Yazdır",
        ["ItemSlipsPrinting"]     = "Stok Fişi Yazdır",
        ["ItemBarcodePrinting"]   = "Barkod Yazdır",
        ["ItemSlipsItemBarcodePrinting"]      = "Fiş Kalem Barkodu Yazdır",
        ["ItemSlipsItemsBatchBarcodePrinting"]= "Toplu Barkod Yazdır",
        ["SafeDepositsPrinting"]  = "Kasa Fişi Yazdır",
        ["StatementsHeaderPrinting"] = "Hesap Özeti Yazdır",
        ["CheckAndPNotesMainPrinting"] = "Çek/Senet Yazdır",
        // Üretim / BOM
        ["ExecuteOpenRecipeAnalysis"] = "Açık Reçete Analiz",
        ["GenerateProductMaterialCosts"] = "Ürün Malzeme Maliyeti",
        ["GenerateProductionOrderItems"] = "Üretim Emri Kalemleri",
        ["GenerateRequirementPlan"] = "İhtiyaç Planı Oluştur",
        ["MaterialRequirementsPlanning"] = "Malzeme İhtiyaç Planı",
        ["ProductionFlowToFinishedGoodsReceipt"] = "Üretim → Mamul Girişi",
        ["ProcessToItemTransactions"] = "Stok Hareketlerine Aktar",
        ["ProcessToItemTransactionsCancelation"] = "Stok Hareketi İptali",
        ["ReceiptProduce"]        = "Makbuz Üret",
        // Ödeme
        ["FastPayRefund"]         = "Hızlı Ödeme İadesi",
        ["PrepareManualSettlement"] = "Manuel Mutabakat Hazırla",
        ["SaveManualSettlement"]  = "Manuel Mutabakat Kaydet",
        ["CancelManualSettlement"]= "Manuel Mutabakat İptal",
        ["CheckPNotesStatementPayment"] = "Çek/Senet Ödeme Kontrolü",
        ["DebitCheckPNotesStatementPayment"] = "Çek/Senet Borç Ödemesi",
        ["SettlementByAging"]     = "Vade Bazlı Mutabakat",
        ["NetRS"]                 = "Net RS Sonucu",
        // Kilitleme
        ["GetBOMLocking"]         = "BOM Kilit Durumu",
        ["SaveBOMLocking"]        = "BOM Kilitle",
        ["GetGLSlipsLocking"]     = "Fiş Kilit Durumu",
        ["SaveGLSlipsLocking"]    = "Fişleri Kilitle",
        ["GetItemSlipsLocking"]   = "Stok Fişi Kilit Durumu",
        ["SaveItemSlipsLocking"]  = "Stok Fişlerini Kilitle",
        ["GetMaintenanceOrderLocking"] = "Bakım Emri Kilit Durumu",
        ["SaveMaintenanceOrderLocking"] = "Bakım Emrini Kilitle",
        ["GetProductionOrderLocking"] = "Üretim Emri Kilit Durumu",
        ["SaveProductionOrderLocking"] = "Üretim Emrini Kilitle",
        // Esnek Yapılandırma
        ["GetFlexibleConfigurations"]         = "Esnek Yapılandırmalar",
        ["AddFlexibleConfigurations"]         = "Esnek Yapılandırma Ekle",
        ["DefineFlexibleConfigurationProperties"] = "Esnek Özellik Tanımla",
        ["DeleteFlexibleConfiguration"]       = "Esnek Yapılandırma Sil",
        // E-posta / Raporlama
        ["SendEmail"]             = "E-posta Gönder",
        ["SendEMailDesign"]       = "E-posta Tasarımı Gönder",
        ["ExecuteReport"]         = "Rapor Çalıştır",
        ["ExecuteVisualReportDraft"] = "Görsel Rapor Taslağı",
        ["GetHtmlItemSlips"]      = "HTML Stok Fişi",
        // Diğer
        ["CustomCustomerMainInfo"] = "Özel Cari Bilgisi",
        ["CustomStockMainInfo"]   = "Özel Stok Bilgisi",
        ["CopyItemSlip"]          = "Stok Fişi Kopyala",
        ["DeleteItemSlipLinkedWaybill"] = "Bağlı İrsaliyeyi Sil",
        ["RecipeStockLinkCreate"] = "Reçete-Stok Bağlantısı",
        ["RefreshExRatesInternal"]= "Döviz Kurlarını Güncelle",
        ["JournalCheck"]          = "Muhasebe Kontrolü",
        ["MontlyVoucherTransfer"] = "Aylık Fiş Transferi",
        ["SetModuleProcessType"]  = "Modül İşlem Tipi",
        ["PrepareOrderBalancing"] = "Sipariş Dengeleme Hazırla",
        ["SaveOrderBalancing"]    = "Sipariş Dengelemeyi Kaydet",
        ["PrepareProductionOrderBalancing"] = "Üretim Emri Dengeleme",
        ["SaveProductionOrderBalancing"] = "Üretim Emri Dengeleme Kaydet",
        ["PrepareRequisitionBalancing"] = "Talep Dengeleme Hazırla",
        ["SaveRequisitionBalancing"] = "Talep Dengelemeyi Kaydet",
        ["PrepareProductionOrderItemList"] = "Üretim Emri Kalem Listesi",
        ["GetFromProductionOrder"]= "Üretim Emrinden Getir",
        ["GetItemCountingRecords"]= "Sayım Kayıtları",
        ["GetItemPlanningRecord"] = "Stok Planlama Kaydı",
        ["DeleteItemPlanningRecord"] = "Stok Planlama Sil",
        ["DeleteCurAccPlanningRecord"] = "Cari Planlama Sil",
        ["DeleteCustomerSupplierItemPlanningRecord"] = "Müşteri/Tedarikçi Planlama Sil",
        ["DeleteBOMItem"]         = "BOM Kalemi Sil",
        ["StockMotionCheck"]      = "Stok Hareket Kontrolü",
        ["GLAccountsList"]        = "Muhasebe Hesap Listesi",
    };

    private static readonly Regex CamelSplitRx = new(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    private static string BuildSummary(string methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return string.Empty;
        if (SummaryMap.TryGetValue(methodName, out var mapped)) return mapped;
        // Bilinmeyen: camelCase → kelime boşlukları
        return CamelSplitRx.Replace(methodName, " ");
    }

    /// <summary>
    /// Resource adina gore kategori cikar (Sablon Galerisi chip filtreleri ile uyumlu).
    /// </summary>
    private static string GuessCategory(string resource)
    {
        var r = resource.ToLowerInvariant();

        // Order — daha spesifik onceki
        if (r.Contains("edocument") || r.Contains("einvoice") || r.Contains("earchive") || r.Contains("edispatch"))
            return "EDocument";
        if (r.StartsWith("bank") || r.Contains("checkandpnote") || r.Contains("fastpay") || r.Contains("payroll"))
            return "Bank";
        if (r.StartsWith("arp") || r.Contains("customer") || r.Contains("supplier") || r.Contains("cari"))
            return "Customer";
        if (r == "items" || r == "item" || r.Contains("stock") || r.Contains("warehouse") ||
            r.Contains("warehs") || r == "bom" || r.Contains("inventory") ||
            r.Contains("finishedgoods") || r.Contains("flexibleconfiguration"))
            return "Stock";
        if (r.Contains("itemslips") || r == "demandoffer" || r.Contains("sales") || r.Contains("contracts") ||
            r.Contains("documentlocking") || r.Contains("batchinvoicing"))
            return "Sales";
        if (r.Contains("purchase") || r.Contains("receipt") || r.Contains("foreigntrade"))
            return "Purchase";

        return "Other";
    }

    /// <summary>
    /// Basit CSV parser: cift-tirnak literal'larini destekler, virgulle ayirir.
    /// Header satirini atlar (ilk hucresi "Resource" ise).
    /// </summary>
    private static List<List<string>> ParseCsv(string csvText)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrWhiteSpace(csvText)) return rows;

        var lines = csvText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        bool firstNonEmpty = true;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = new List<string>();
            var cur = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') inQuote = true;
                    else if (c == ',') { cells.Add(cur.ToString()); cur.Clear(); }
                    else cur.Append(c);
                }
            }
            cells.Add(cur.ToString());

            if (firstNonEmpty)
            {
                firstNonEmpty = false;
                if (cells.Count > 0 &&
                    string.Equals(cells[0]?.Trim(), "Resource", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            rows.Add(cells);
        }

        return rows;
    }
}
