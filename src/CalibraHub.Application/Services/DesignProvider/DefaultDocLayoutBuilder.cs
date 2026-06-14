using System.Text.Json;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.DesignProvider;

/// <summary>
/// Sistem kurulumunda her belge tipi için varsayılan bir DocLayout (orta-süslü tasarım)
/// oluşturur. Layout JSON'ı parametrik olarak üretir: belge tipi adı, view adı ve key
/// kolonuna göre PageHeader / Info / TableHeader / Detail / TableFooter / PageFooter
/// bantları içeren A4 sayfası kurar.
///
/// 5 template varyantı:
///   - DocumentPdf (vw_ReportDocument) : satis_teklifi/siparisi, alis_talebi/teklifi/siparisi,
///                                        satin_alma_talebi
///   - InvoicePdf  (vw_Invoice)        : fatura
///   - DispatchPdf (vw_DeliveryNote)   : irsaliye
///   - AssetPdf    (vw_AssetAssignment): zimmet_teslim
///   - LabelPdf    (vw_ProductBarcode / vw_ShelfLabel) : urun_barkodu, raf_etiketi
///   - MailHtml    : mail_template
///   - BlankPdf    : is_emri, arge_proje (view yok, başlangıç şablonu)
///
/// Her template kendi <see cref="ColumnMap"/>'ini kullanır → gerçek view kolonları ile
/// doğru eşleşir. Üretilen JSON, DocLayoutRenderer'ın <c>LayoutDoc</c> şemasıyla uyumludur.
/// </summary>
public static class DefaultDocLayoutBuilder
{
    /// <summary>Standart accent (indigo) — başlık + ayraçlar.</summary>
    private const string AccentColor = "#6366f1";
    private const string TextColor   = "#0f172a";
    private const string MutedColor  = "#64748b";
    private const string LightBg     = "#f1f5f9";

    /// <summary>
    /// Verilen belge tipi için varsayılan layout meta + JSON üretir.
    /// </summary>
    public static DefaultLayoutResult Build(
        string docTypeCode,
        string docTypeName,
        string? viewName,
        string? requiredKeyColumn)
    {
        var template = ResolveTemplate(docTypeCode);
        var meta     = ResolveMeta(template);
        var map      = ResolveColumnMap(docTypeCode);

        var bands = template switch
        {
            "LabelPdf"    => BuildLabelBands(map),
            "MailHtml"    => BuildMailBands(map),
            "BlankPdf"    => BuildBlankBands(docTypeName),
            "InvoicePdf"  => BuildInvoiceBands(docTypeName, map),
            "DispatchPdf" => BuildDispatchBands(docTypeName, map),
            "AssetPdf"    => BuildAssetBands(docTypeName, map),
            _             => BuildDocumentBands(docTypeCode, docTypeName, map),
        };

        var json = SerializeLayout(meta, bands);
        return new DefaultLayoutResult(meta, json, template);
    }

    public static IReadOnlyList<DocLayoutDs> BuildDataSources(
        int layoutId,
        string docTypeCode,
        string? viewName,
        string? requiredKeyColumn)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            return Array.Empty<DocLayoutDs>();

        var keyCol = string.IsNullOrWhiteSpace(requiredKeyColumn) ? "BelgeId" : requiredKeyColumn;
        return new[]
        {
            new DocLayoutDs
            {
                LayoutId    = layoutId,
                Alias       = "Master",
                Role        = "master",
                ViewId      = null,
                AdHocSql    = $"SELECT * FROM [dbo].[{viewName}] WHERE [{keyCol}] = @DocumentId",
                JoinOn      = null,
                ParentAlias = null,
                Ordinal     = 0,
            },
        };
    }

    // ── Template selection ─────────────────────────────────────────────────────

    private static string ResolveTemplate(string docTypeCode) => docTypeCode.ToLowerInvariant() switch
    {
        "fatura"                       => "InvoicePdf",
        "irsaliye"                     => "DispatchPdf",
        "zimmet_teslim"                => "AssetPdf",
        "urun_barkodu" or "raf_etiketi" => "LabelPdf",
        "mail_template"                => "MailHtml",
        "is_emri" or "arge_proje"       => "BlankPdf",
        _                               => "DocumentPdf",
    };

    private static LayoutMeta ResolveMeta(string template) => template switch
    {
        "LabelPdf"  => new LayoutMeta(PageW: 80m, PageH: 40m, MarginTop: 2m, MarginBot: 2m, MarginLeft: 2m, MarginRight: 2m),
        "MailHtml"  => new LayoutMeta(PageW: 210m, PageH: 297m, MarginTop: 12m, MarginBot: 12m, MarginLeft: 15m, MarginRight: 15m),
        _           => new LayoutMeta(PageW: 210m, PageH: 297m, MarginTop: 10m, MarginBot: 10m, MarginLeft: 15m, MarginRight: 10m),
    };

    // ── Column map: her view'in gerçek kolon adları ────────────────────────────

    /// <summary>
    /// Logical alan adlarını ilgili view'in gerçek kolon adına eşleyen sözlük.
    /// Her belge tipi farklı view kullandığında kolon adları farklı olur — bu yüzden
    /// template merkezi şekilde "BelgeNo" gibi logical bir isim üzerinden bağlar,
    /// burada gerçek SQL kolon adına çevrilir.
    /// </summary>
    private sealed record ColumnMap(
        // Belge başlığı
        string? DocNo, string? DocDate, string? ValidUntil,
        // Cari/Müşteri
        string? CustomerName, string? CustomerAddress, string? CustomerTax,
        // Belge bilgisi
        string? PaymentTerms, string? Currency, string? SalesRep,
        // Satır
        string? LineNo, string? MaterialCode, string? MaterialName,
        string? Quantity, string? Unit, string? UnitPrice, string? LineTax, string? LineTotal,
        // Toplam blok
        string? SubTotal, string? Discount, string? TotalTax, string? GrandTotal,
        // Footer
        string? Notes);

    private static ColumnMap ResolveColumnMap(string docTypeCode) => docTypeCode.ToLowerInvariant() switch
    {
        // Document tabanlı (vw_ReportDocument) — 6 belge tipi
        "satis_teklifi" or "satis_siparisi" or "alis_talebi" or "alis_teklifi" or
        "alis_siparisi" or "satin_alma_talebi" => new ColumnMap(
            DocNo:            "BelgeNo",
            DocDate:          "BelgeTarihi",
            ValidUntil:       "GecerlilikTarihi",
            CustomerName:     "CariUnvani",
            CustomerAddress:  "CariTamAdres",
            CustomerTax:      "CariVergiSatiri",
            PaymentTerms:     "OdemeKosullari",
            Currency:         "ParaBirimi",
            SalesRep:         "TemsilciAdi",
            LineNo:           "SiraNo",
            MaterialCode:     "MalzemeKodu",
            MaterialName:     "MalzemeAdi",
            Quantity:         "Miktar",
            Unit:             "BirimAdi",
            UnitPrice:        "BirimFiyat",
            LineTax:          "MalzemeKdvOrani",
            LineTotal:        "SatirToplami",
            SubTotal:         "AraToplam",
            Discount:         "IskontoTutari",
            TotalTax:         "KdvTutari",
            GrandTotal:       "GenelToplam",
            Notes:            "BelgeNotu"),

        // Fatura (vw_Invoice) — İngilizce kolon adları
        "fatura" => new ColumnMap(
            DocNo:           "DocumentNumber",
            DocDate:         "DocumentDate",
            ValidUntil:      null,
            CustomerName:    "CustomerName",
            CustomerAddress: "CustomerAddress",
            CustomerTax:     null,
            PaymentTerms:    "PaymentTerms",
            Currency:        "currency",
            SalesRep:        null,
            LineNo:          "LineNo",
            MaterialCode:    "MaterialCode",
            MaterialName:    "MaterialName",
            Quantity:        "Quantity",
            Unit:            "UnitName",
            UnitPrice:       "UnitPrice",
            LineTax:         null,
            LineTotal:       "LineTotal",
            SubTotal:        "SubTotal",
            Discount:        "DiscountAmount",
            TotalTax:        "TaxAmount",
            GrandTotal:      "GrandTotal",
            Notes:           "Notes"),

        // İrsaliye (vw_DeliveryNote)
        "irsaliye" => new ColumnMap(
            DocNo:           "DocumentNumber",
            DocDate:         "DocumentDate",
            ValidUntil:      null,
            CustomerName:    "CustomerName",
            CustomerAddress: "DeliveryAddress",
            CustomerTax:     null,
            PaymentTerms:    "DeliveryTerms",
            Currency:        null,
            SalesRep:        null,
            LineNo:          "LineNo",
            MaterialCode:    "MaterialCode",
            MaterialName:    "MaterialName",
            Quantity:        "Quantity",
            Unit:            "UnitName",
            UnitPrice:       null,
            LineTax:         null,
            LineTotal:       null,
            SubTotal:        null,
            Discount:        null,
            TotalTax:        null,
            GrandTotal:      null,
            Notes:           null),

        // Etiket (vw_ProductBarcode / vw_ShelfLabel) — id/ProductCode/ProductName
        "urun_barkodu" or "raf_etiketi" => new ColumnMap(
            DocNo:           null, DocDate: null, ValidUntil: null,
            CustomerName:    null, CustomerAddress: null, CustomerTax: null,
            PaymentTerms:    null, Currency: null, SalesRep: null,
            LineNo:          null,
            MaterialCode:    "ProductCode",
            MaterialName:    "ProductName",
            Quantity:        null,
            Unit:            "UnitName",
            UnitPrice:       null,
            LineTax:         null, LineTotal: null,
            SubTotal:        null, Discount: null, TotalTax: null, GrandTotal: null,
            Notes:           null),

        // Zimmet teslim (vw_AssetAssignment)
        "zimmet_teslim" => new ColumnMap(
            DocNo:           "DocumentNo",
            DocDate:         "AssignDate",
            ValidUntil:      "ReturnDate",
            CustomerName:    "PersonnelName",     // Burada cari yerine personel
            CustomerAddress: "DepartmentName",    // Bölüm
            CustomerTax:     null,
            PaymentTerms:    null,
            Currency:        null,
            SalesRep:        "CreatedByName",
            LineNo:          null,
            MaterialCode:    "AssetCode",
            MaterialName:    "AssetName",
            Quantity:        "SerialNo",          // Etiket yerine seri no
            Unit:            null,
            UnitPrice:       null,
            LineTax:         null, LineTotal: null,
            SubTotal:        null, Discount: null, TotalTax: null, GrandTotal: null,
            Notes:           "AssignNote"),

        _ => new ColumnMap(null, null, null, null, null, null, null, null, null,
                           null, null, null, null, null, null, null, null,
                           null, null, null, null, null),
    };

    // ── DocumentPdf (vw_ReportDocument) — 6 belge tipi ────────────────────────

    private static List<object> BuildDocumentBands(string docTypeCode, string docTypeName, ColumnMap m)
    {
        var alias = "Master";
        var isProcurement = docTypeCode.StartsWith("alis_") || docTypeCode == "satin_alma_talebi";

        return new List<object>
        {
            // PageHeader (40mm)
            Band("page-header", "PageHeader", 40, true, null, new List<object>
            {
                Element("hdr-logo", "Image", 0, 2, 35, 20,
                    imageFit: "contain",
                    style: Style(border: true)),
                Element("hdr-title", "Label", 80, 2, 105, 14,
                    text: docTypeName.ToUpperInvariant(),
                    style: Style(fontSize: 22, bold: true, color: AccentColor, align: "right")),
                Element("hdr-no-lbl", "Text", 80, 18, 105, 6,
                    text: "No:",
                    style: Style(fontSize: 9, color: MutedColor, align: "right")),
                BoundOrPlaceholder("hdr-no-val", "Text", 80, 24, 105, 7,
                    alias, m.DocNo,
                    style: Style(fontSize: 13, bold: true, color: TextColor, align: "right")),
                Element("hdr-date-lbl", "Text", 80, 33, 50, 5,
                    text: "Tarih:",
                    style: Style(fontSize: 9, color: MutedColor, align: "right")),
                BoundOrPlaceholder("hdr-date-val", "Text", 132, 33, 53, 5,
                    alias, m.DocDate, format: "{:dd.MM.yyyy}",
                    style: Style(fontSize: 10, color: TextColor, align: "left")),
                Element("hdr-line", "Shape", 0, 39, 185, 0.6,
                    shapeKind: "Line",
                    style: Style(color: AccentColor)),
            }),

            // Info bandı (38mm)
            Band("info", "PageHeader", 38, true, null, new List<object>
            {
                // Sol blok: cari
                Element("info-cust-bg", "Shape", 0, 2, 88, 34,
                    shapeKind: "Rectangle",
                    style: Style(bgColor: LightBg, border: true, color: "#e2e8f0")),
                Element("info-cust-title", "Text", 3, 4, 82, 6,
                    text: isProcurement ? "TEDARİKÇİ" : "MÜŞTERİ",
                    style: Style(fontSize: 9, bold: true, color: AccentColor, align: "left")),
                BoundOrPlaceholder("info-cust-name", "Text", 3, 11, 82, 6,
                    alias, m.CustomerName,
                    style: Style(fontSize: 11, bold: true, color: TextColor, align: "left")),
                Element("info-cust-addr-lbl", "Text", 3, 18, 18, 4,
                    text: "Adres:",
                    style: Style(fontSize: 8, color: MutedColor, align: "left")),
                BoundOrPlaceholder("info-cust-addr", "Text", 21, 18, 64, 8,
                    alias, m.CustomerAddress,
                    style: Style(fontSize: 9, color: TextColor, align: "left")),
                Element("info-cust-tax-lbl", "Text", 3, 27, 25, 4,
                    text: "V.D./No:",
                    style: Style(fontSize: 8, color: MutedColor, align: "left")),
                BoundOrPlaceholder("info-cust-tax", "Text", 28, 27, 57, 4,
                    alias, m.CustomerTax,
                    style: Style(fontSize: 9, color: TextColor, align: "left")),

                // Sağ blok: belge bilgileri
                Element("info-doc-bg", "Shape", 97, 2, 88, 34,
                    shapeKind: "Rectangle",
                    style: Style(bgColor: LightBg, border: true, color: "#e2e8f0")),
                Element("info-doc-title", "Text", 100, 4, 82, 6,
                    text: "BELGE BİLGİLERİ",
                    style: Style(fontSize: 9, bold: true, color: AccentColor, align: "left")),

                Element("info-doc-valid-lbl", "Text", 100, 11, 35, 4,
                    text: "Geçerlilik:",
                    style: Style(fontSize: 8, color: MutedColor, align: "left")),
                BoundOrPlaceholder("info-doc-valid", "Text", 137, 11, 45, 4,
                    alias, m.ValidUntil, format: "{:dd.MM.yyyy}",
                    style: Style(fontSize: 9, color: TextColor, align: "left")),

                Element("info-doc-pay-lbl", "Text", 100, 17, 35, 4,
                    text: "Vade:",
                    style: Style(fontSize: 8, color: MutedColor, align: "left")),
                BoundOrPlaceholder("info-doc-pay", "Text", 137, 17, 45, 4,
                    alias, m.PaymentTerms,
                    style: Style(fontSize: 9, color: TextColor, align: "left")),

                Element("info-doc-cur-lbl", "Text", 100, 23, 35, 4,
                    text: "Para Birimi:",
                    style: Style(fontSize: 8, color: MutedColor, align: "left")),
                BoundOrPlaceholder("info-doc-cur", "Text", 137, 23, 45, 4,
                    alias, m.Currency,
                    style: Style(fontSize: 9, color: TextColor, align: "left")),

                Element("info-doc-rep-lbl", "Text", 100, 29, 35, 4,
                    text: "Sorumlu:",
                    style: Style(fontSize: 8, color: MutedColor, align: "left")),
                BoundOrPlaceholder("info-doc-rep", "Text", 137, 29, 45, 4,
                    alias, m.SalesRep,
                    style: Style(fontSize: 9, color: TextColor, align: "left")),
            }),

            // Table Header (8mm) — 8 kolonlu
            BuildStandardTableHeader(),

            // Detail (7mm) — repeating
            Band("detail", "Detail", 7, false, alias, new List<object>
            {
                BoundOrPlaceholder("d-sira", "Text", 1, 0, 8, 7,
                    alias, m.LineNo,
                    style: Style(fontSize: 8.5f, color: MutedColor, align: "center", verticalAlign: "middle")),
                BoundOrPlaceholder("d-kod", "Text", 9, 0, 25, 7,
                    alias, m.MaterialCode,
                    style: Style(fontSize: 8.5f, color: TextColor, align: "left", verticalAlign: "middle")),
                BoundOrPlaceholder("d-aciklama", "Text", 34, 0, 70, 7,
                    alias, m.MaterialName,
                    style: Style(fontSize: 8.5f, color: TextColor, align: "left", verticalAlign: "middle", overflow: "ellipsis")),
                BoundOrPlaceholder("d-miktar", "Text", 104, 0, 14, 7,
                    alias, m.Quantity, format: "{:N2}",
                    style: Style(fontSize: 8.5f, color: TextColor, align: "right", verticalAlign: "middle")),
                BoundOrPlaceholder("d-birim", "Text", 118, 0, 12, 7,
                    alias, m.Unit,
                    style: Style(fontSize: 8.5f, color: TextColor, align: "center", verticalAlign: "middle")),
                BoundOrPlaceholder("d-bfiyat", "Text", 130, 0, 18, 7,
                    alias, m.UnitPrice, format: "{:N2}",
                    style: Style(fontSize: 8.5f, color: TextColor, align: "right", verticalAlign: "middle")),
                BoundOrPlaceholder("d-kdv", "Text", 148, 0, 10, 7,
                    alias, m.LineTax, format: "{:N0}",
                    style: Style(fontSize: 8.5f, color: TextColor, align: "right", verticalAlign: "middle")),
                BoundOrPlaceholder("d-toplam", "Text", 158, 0, 27, 7,
                    alias, m.LineTotal, format: "{:N2}",
                    style: Style(fontSize: 8.5f, bold: true, color: TextColor, align: "right", verticalAlign: "middle")),
            }, canGrow: true, zebraEnabled: true, zebraColor: "#f8fafc"),

            // Tablo Footer (32mm) — toplam blok
            Band("tbl-foot", "TableFooter", 32, false, null, new List<object>
            {
                Element("tf-bg", "Shape", 120, 2, 65, 28,
                    shapeKind: "Rectangle",
                    style: Style(bgColor: LightBg, border: true, color: "#e2e8f0")),
                Element("tf-sub-lbl", "Text", 123, 5, 32, 5,
                    text: "Ara Toplam:",
                    style: Style(fontSize: 9, color: MutedColor, align: "left")),
                BoundOrPlaceholder("tf-sub", "Text", 155, 5, 27, 5,
                    alias, m.SubTotal, format: "{:N2}",
                    style: Style(fontSize: 9.5f, color: TextColor, align: "right")),
                Element("tf-disc-lbl", "Text", 123, 11, 32, 5,
                    text: "İskonto:",
                    style: Style(fontSize: 9, color: MutedColor, align: "left")),
                BoundOrPlaceholder("tf-disc", "Text", 155, 11, 27, 5,
                    alias, m.Discount, format: "{:N2}",
                    style: Style(fontSize: 9.5f, color: TextColor, align: "right")),
                Element("tf-tax-lbl", "Text", 123, 17, 32, 5,
                    text: "KDV:",
                    style: Style(fontSize: 9, color: MutedColor, align: "left")),
                BoundOrPlaceholder("tf-tax", "Text", 155, 17, 27, 5,
                    alias, m.TotalTax, format: "{:N2}",
                    style: Style(fontSize: 9.5f, color: TextColor, align: "right")),
                Element("tf-sep", "Shape", 123, 23, 59, 0.5,
                    shapeKind: "Line",
                    style: Style(color: AccentColor)),
                Element("tf-grand-lbl", "Text", 123, 25, 32, 6,
                    text: "GENEL TOPLAM:",
                    style: Style(fontSize: 10, bold: true, color: AccentColor, align: "left")),
                BoundOrPlaceholder("tf-grand", "Text", 155, 25, 27, 6,
                    alias, m.GrandTotal, format: "{:N2}",
                    style: Style(fontSize: 11, bold: true, color: AccentColor, align: "right")),
            }),

            // Page Footer (15mm)
            BuildStandardPageFooter(alias, m.Notes),
        };
    }

    // ── InvoicePdf (vw_Invoice) ───────────────────────────────────────────────

    private static List<object> BuildInvoiceBands(string docTypeName, ColumnMap m)
        => BuildDocumentBands("fatura", docTypeName, m);  // Aynı yapı — sadece kolonlar farklı

    // ── DispatchPdf (vw_DeliveryNote) — kalem fiyatı yok ──────────────────────

    private static List<object> BuildDispatchBands(string docTypeName, ColumnMap m)
    {
        var alias = "Master";
        return new List<object>
        {
            Band("page-header", "PageHeader", 40, true, null, new List<object>
            {
                Element("hdr-logo", "Image", 0, 2, 35, 20,
                    imageFit: "contain", style: Style(border: true)),
                Element("hdr-title", "Label", 80, 2, 105, 14,
                    text: docTypeName.ToUpperInvariant(),
                    style: Style(fontSize: 22, bold: true, color: AccentColor, align: "right")),
                BoundOrPlaceholder("hdr-no-val", "Text", 80, 22, 105, 7,
                    alias, m.DocNo,
                    style: Style(fontSize: 13, bold: true, color: TextColor, align: "right")),
                BoundOrPlaceholder("hdr-date-val", "Text", 80, 32, 105, 5,
                    alias, m.DocDate, format: "{:dd.MM.yyyy}",
                    style: Style(fontSize: 10, color: MutedColor, align: "right")),
                Element("hdr-line", "Shape", 0, 39, 185, 0.6,
                    shapeKind: "Line", style: Style(color: AccentColor)),
            }),

            Band("info", "PageHeader", 28, true, null, new List<object>
            {
                Element("info-cust-bg", "Shape", 0, 2, 88, 24,
                    shapeKind: "Rectangle", style: Style(bgColor: LightBg, border: true, color: "#e2e8f0")),
                Element("info-cust-title", "Text", 3, 4, 82, 6,
                    text: "MÜŞTERİ",
                    style: Style(fontSize: 9, bold: true, color: AccentColor)),
                BoundOrPlaceholder("info-cust-name", "Text", 3, 11, 82, 6,
                    alias, m.CustomerName,
                    style: Style(fontSize: 11, bold: true, color: TextColor)),
                BoundOrPlaceholder("info-cust-addr", "Text", 3, 18, 82, 6,
                    alias, m.CustomerAddress,
                    style: Style(fontSize: 9, color: TextColor)),

                Element("info-ship-bg", "Shape", 97, 2, 88, 24,
                    shapeKind: "Rectangle", style: Style(bgColor: LightBg, border: true, color: "#e2e8f0")),
                Element("info-ship-title", "Text", 100, 4, 82, 6,
                    text: "SEVK BİLGİLERİ",
                    style: Style(fontSize: 9, bold: true, color: AccentColor)),
                Element("info-ship-terms-lbl", "Text", 100, 11, 25, 4,
                    text: "Şekli:",
                    style: Style(fontSize: 8, color: MutedColor)),
                BoundOrPlaceholder("info-ship-terms", "Text", 127, 11, 55, 4,
                    alias, m.PaymentTerms,
                    style: Style(fontSize: 9, color: TextColor)),
            }),

            // İrsaliye tablosu — 4 kolon (Sıra, Kod, Açıklama, Miktar)
            Band("tbl-head", "TableHeader", 8, true, null, new List<object>
            {
                Element("th-bg", "Shape", 0, 0, 185, 8,
                    shapeKind: "Rectangle", style: Style(bgColor: AccentColor)),
                Element("th-sira", "Text", 1, 0, 10, 8,
                    text: "#", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "center", verticalAlign: "middle")),
                Element("th-kod", "Text", 11, 0, 35, 8,
                    text: "Malzeme Kodu", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "left", verticalAlign: "middle")),
                Element("th-aciklama", "Text", 46, 0, 100, 8,
                    text: "Malzeme Adı", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "left", verticalAlign: "middle")),
                Element("th-miktar", "Text", 146, 0, 25, 8,
                    text: "Miktar", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "right", verticalAlign: "middle")),
                Element("th-birim", "Text", 171, 0, 14, 8,
                    text: "Birim", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "center", verticalAlign: "middle")),
            }),
            Band("detail", "Detail", 7, false, alias, new List<object>
            {
                BoundOrPlaceholder("d-sira", "Text", 1, 0, 10, 7,
                    alias, m.LineNo,
                    style: Style(fontSize: 8.5f, color: MutedColor, align: "center", verticalAlign: "middle")),
                BoundOrPlaceholder("d-kod", "Text", 11, 0, 35, 7,
                    alias, m.MaterialCode,
                    style: Style(fontSize: 8.5f, color: TextColor, align: "left", verticalAlign: "middle")),
                BoundOrPlaceholder("d-aciklama", "Text", 46, 0, 100, 7,
                    alias, m.MaterialName,
                    style: Style(fontSize: 8.5f, color: TextColor, align: "left", verticalAlign: "middle", overflow: "ellipsis")),
                BoundOrPlaceholder("d-miktar", "Text", 146, 0, 25, 7,
                    alias, m.Quantity, format: "{:N2}",
                    style: Style(fontSize: 8.5f, color: TextColor, align: "right", verticalAlign: "middle")),
                BoundOrPlaceholder("d-birim", "Text", 171, 0, 14, 7,
                    alias, m.Unit,
                    style: Style(fontSize: 8.5f, color: TextColor, align: "center", verticalAlign: "middle")),
            }, canGrow: true, zebraEnabled: true, zebraColor: "#f8fafc"),

            // Page Footer — irsaliye için imza alanları
            Band("page-footer", "PageFooter", 30, true, null, new List<object>
            {
                Element("pf-sep", "Shape", 0, 0, 185, 0.3,
                    shapeKind: "Line", style: Style(color: "#cbd5e1")),

                // İmza kutucukları
                Element("pf-sign1-bg", "Shape", 0, 5, 60, 22,
                    shapeKind: "Rectangle",
                    style: Style(border: true, color: "#cbd5e1")),
                Element("pf-sign1-title", "Text", 0, 6, 60, 4,
                    text: "TESLİM EDEN",
                    style: Style(fontSize: 8, bold: true, color: MutedColor, align: "center")),

                Element("pf-sign2-bg", "Shape", 65, 5, 60, 22,
                    shapeKind: "Rectangle",
                    style: Style(border: true, color: "#cbd5e1")),
                Element("pf-sign2-title", "Text", 65, 6, 60, 4,
                    text: "ARAÇ ŞOFÖRÜ",
                    style: Style(fontSize: 8, bold: true, color: MutedColor, align: "center")),

                Element("pf-sign3-bg", "Shape", 130, 5, 55, 22,
                    shapeKind: "Rectangle",
                    style: Style(border: true, color: "#cbd5e1")),
                Element("pf-sign3-title", "Text", 130, 6, 55, 4,
                    text: "TESLİM ALAN",
                    style: Style(fontSize: 8, bold: true, color: MutedColor, align: "center")),
            }),
        };
    }

    // ── AssetPdf (vw_AssetAssignment) — zimmet teslim formu ───────────────────

    private static List<object> BuildAssetBands(string docTypeName, ColumnMap m)
    {
        var alias = "Master";
        return new List<object>
        {
            Band("page-header", "PageHeader", 40, true, null, new List<object>
            {
                Element("hdr-logo", "Image", 0, 2, 35, 20,
                    imageFit: "contain", style: Style(border: true)),
                Element("hdr-title", "Label", 50, 2, 135, 14,
                    text: docTypeName.ToUpperInvariant(),
                    style: Style(fontSize: 20, bold: true, color: AccentColor, align: "center")),
                BoundOrPlaceholder("hdr-no-val", "Text", 50, 22, 135, 6,
                    alias, m.DocNo,
                    style: Style(fontSize: 11, color: MutedColor, align: "center")),
                Element("hdr-line", "Shape", 0, 39, 185, 0.6,
                    shapeKind: "Line", style: Style(color: AccentColor)),
            }),

            // Personel + Zimmet bilgileri
            Band("info", "PageHeader", 50, true, null, new List<object>
            {
                Element("info-personnel-title", "Text", 0, 2, 185, 6,
                    text: "ZİMMET BİLGİLERİ",
                    style: Style(fontSize: 10, bold: true, color: AccentColor)),
                Element("info-personnel-line", "Shape", 0, 9, 185, 0.3,
                    shapeKind: "Line", style: Style(color: "#cbd5e1")),

                // 2 sütun
                Element("info-personnel-lbl", "Text", 0, 12, 35, 5,
                    text: "Teslim Alan:", style: Style(fontSize: 9, color: MutedColor)),
                BoundOrPlaceholder("info-personnel-val", "Text", 37, 12, 80, 5,
                    alias, m.CustomerName,
                    style: Style(fontSize: 10, bold: true, color: TextColor)),

                Element("info-dept-lbl", "Text", 0, 19, 35, 5,
                    text: "Departman:", style: Style(fontSize: 9, color: MutedColor)),
                BoundOrPlaceholder("info-dept-val", "Text", 37, 19, 80, 5,
                    alias, m.CustomerAddress,
                    style: Style(fontSize: 10, color: TextColor)),

                Element("info-date-lbl", "Text", 0, 26, 35, 5,
                    text: "Zimmet Tarihi:", style: Style(fontSize: 9, color: MutedColor)),
                BoundOrPlaceholder("info-date-val", "Text", 37, 26, 80, 5,
                    alias, m.DocDate, format: "{:dd.MM.yyyy}",
                    style: Style(fontSize: 10, color: TextColor)),

                Element("info-ret-lbl", "Text", 0, 33, 35, 5,
                    text: "İade Tarihi:", style: Style(fontSize: 9, color: MutedColor)),
                BoundOrPlaceholder("info-ret-val", "Text", 37, 33, 80, 5,
                    alias, m.ValidUntil, format: "{:dd.MM.yyyy}",
                    style: Style(fontSize: 10, color: TextColor)),

                Element("info-by-lbl", "Text", 0, 40, 35, 5,
                    text: "Tutanağı Düzenleyen:", style: Style(fontSize: 9, color: MutedColor)),
                BoundOrPlaceholder("info-by-val", "Text", 37, 40, 80, 5,
                    alias, m.SalesRep,
                    style: Style(fontSize: 10, color: TextColor)),
            }),

            // Varlık tablosu
            Band("tbl-head", "TableHeader", 8, true, null, new List<object>
            {
                Element("th-bg", "Shape", 0, 0, 185, 8,
                    shapeKind: "Rectangle", style: Style(bgColor: AccentColor)),
                Element("th-kod", "Text", 1, 0, 35, 8,
                    text: "Varlık Kodu",
                    style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "left", verticalAlign: "middle")),
                Element("th-name", "Text", 36, 0, 100, 8,
                    text: "Varlık Adı",
                    style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "left", verticalAlign: "middle")),
                Element("th-sn", "Text", 136, 0, 49, 8,
                    text: "Seri No",
                    style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "left", verticalAlign: "middle")),
            }),
            Band("detail", "Detail", 7, false, alias, new List<object>
            {
                BoundOrPlaceholder("d-kod", "Text", 1, 0, 35, 7,
                    alias, m.MaterialCode,
                    style: Style(fontSize: 9, color: TextColor, align: "left", verticalAlign: "middle")),
                BoundOrPlaceholder("d-name", "Text", 36, 0, 100, 7,
                    alias, m.MaterialName,
                    style: Style(fontSize: 9, color: TextColor, align: "left", verticalAlign: "middle")),
                BoundOrPlaceholder("d-sn", "Text", 136, 0, 49, 7,
                    alias, m.Quantity,
                    style: Style(fontSize: 9, color: TextColor, align: "left", verticalAlign: "middle")),
            }, canGrow: true),

            // Page Footer + imza
            Band("page-footer", "PageFooter", 50, true, null, new List<object>
            {
                Element("pf-notes-title", "Text", 0, 0, 185, 5,
                    text: "Açıklama:",
                    style: Style(fontSize: 9, bold: true, color: MutedColor)),
                BoundOrPlaceholder("pf-notes", "Text", 0, 7, 185, 10,
                    alias, m.Notes,
                    style: Style(fontSize: 9, color: TextColor)),

                // Çift imza alanı
                Element("pf-sign1-bg", "Shape", 0, 22, 88, 24,
                    shapeKind: "Rectangle",
                    style: Style(border: true, color: "#cbd5e1")),
                Element("pf-sign1-title", "Text", 0, 24, 88, 4,
                    text: "TESLİM EDEN İMZA",
                    style: Style(fontSize: 8, bold: true, color: MutedColor, align: "center")),

                Element("pf-sign2-bg", "Shape", 97, 22, 88, 24,
                    shapeKind: "Rectangle",
                    style: Style(border: true, color: "#cbd5e1")),
                Element("pf-sign2-title", "Text", 97, 24, 88, 4,
                    text: "TESLİM ALAN İMZA",
                    style: Style(fontSize: 8, bold: true, color: MutedColor, align: "center")),
            }),
        };
    }

    // ── LabelPdf (vw_ProductBarcode / vw_ShelfLabel) — 80x40mm ───────────────

    private static List<object> BuildLabelBands(ColumnMap m)
    {
        var alias = "Master";
        return new List<object>
        {
            Band("label", "Detail", 36, false, alias, new List<object>
            {
                BoundOrPlaceholder("lbl-name", "Text", 0, 0, 76, 6,
                    alias, m.MaterialName,
                    style: Style(fontSize: 9, bold: true, color: TextColor, align: "center", verticalAlign: "middle")),
                BoundOrPlaceholder("lbl-code", "Text", 0, 6, 76, 5,
                    alias, m.MaterialCode,
                    style: Style(fontSize: 8, color: MutedColor, align: "center")),
                ElementBound("lbl-barcode", "Barcode", 8, 12, 60, 18,
                    alias, m.MaterialCode ?? "ProductCode",
                    barcodeType: "Code128", showBarcodeText: true,
                    style: Style(align: "center")),
                BoundOrPlaceholder("lbl-unit", "Text", 0, 31, 76, 5,
                    alias, m.Unit,
                    style: Style(fontSize: 9, color: MutedColor, align: "center")),
            }, canGrow: false),
        };
    }

    // ── MailHtml (mail_template) ──────────────────────────────────────────────

    private static List<object> BuildMailBands(ColumnMap m)
    {
        // Mail şablonu için view yok; placeholder'lar kullanılır.
        return new List<object>
        {
            Band("mail-body", "Detail", 120, false, null, new List<object>
            {
                Element("m-logo", "Image", 60, 0, 60, 25, imageFit: "contain"),

                Element("m-sep1", "Shape", 0, 28, 180, 0.5,
                    shapeKind: "Line", style: Style(color: AccentColor)),

                Element("m-greeting", "Text", 0, 33, 180, 6,
                    text: "Sayın {alici_ad},",
                    style: Style(fontSize: 11, bold: true, color: TextColor)),

                Element("m-body1", "Text", 0, 43, 180, 16,
                    text: "Aşağıda iletilen bilgileri inceleyebilirsiniz. Sorularınız için lütfen bizimle iletişime geçin.",
                    style: Style(fontSize: 10, color: TextColor)),

                Element("m-box-bg", "Shape", 0, 62, 180, 36,
                    shapeKind: "Rectangle",
                    style: Style(bgColor: LightBg, border: true, color: "#e2e8f0")),
                Element("m-box-title", "Text", 4, 65, 172, 6,
                    text: "BELGE BİLGİLERİ",
                    style: Style(fontSize: 9, bold: true, color: AccentColor)),
                Element("m-box-no-lbl", "Text", 4, 73, 35, 5,
                    text: "Belge No:",
                    style: Style(fontSize: 9, color: MutedColor)),
                Element("m-box-no", "Text", 41, 73, 135, 5,
                    text: "{belge_no}",
                    style: Style(fontSize: 10, bold: true, color: TextColor)),
                Element("m-box-date-lbl", "Text", 4, 80, 35, 5,
                    text: "Tarih:",
                    style: Style(fontSize: 9, color: MutedColor)),
                Element("m-box-date", "Text", 41, 80, 135, 5,
                    text: "{belge_tarihi}",
                    style: Style(fontSize: 9, color: TextColor)),
                Element("m-box-amount-lbl", "Text", 4, 87, 35, 5,
                    text: "Tutar:",
                    style: Style(fontSize: 9, color: MutedColor)),
                Element("m-box-amount", "Text", 41, 87, 135, 5,
                    text: "{tutar}",
                    style: Style(fontSize: 10, bold: true, color: AccentColor)),

                Element("m-closing", "Text", 0, 105, 180, 5,
                    text: "Saygılarımızla,",
                    style: Style(fontSize: 10, color: TextColor)),
                Element("m-signature", "Text", 0, 112, 180, 5,
                    text: "{sirket_adi}",
                    style: Style(fontSize: 10, bold: true, color: TextColor)),
            }),
        };
    }

    // ── BlankPdf (is_emri, arge_proje) — view yok, başlangıç şablonu ────────

    private static List<object> BuildBlankBands(string docTypeName)
    {
        return new List<object>
        {
            Band("page-header", "PageHeader", 40, true, null, new List<object>
            {
                Element("hdr-logo", "Image", 0, 2, 35, 20,
                    imageFit: "contain", style: Style(border: true)),
                Element("hdr-title", "Label", 50, 8, 135, 14,
                    text: docTypeName.ToUpperInvariant(),
                    style: Style(fontSize: 22, bold: true, color: AccentColor, align: "center")),
                Element("hdr-line", "Shape", 0, 39, 185, 0.6,
                    shapeKind: "Line", style: Style(color: AccentColor)),
            }),
            Band("content", "Detail", 200, false, null, new List<object>
            {
                Element("placeholder", "Text", 0, 0, 185, 20,
                    text: $"Bu varsayılan {docTypeName} şablonudur. Veri kaynağı eklemek için 'Veri Kaynakları' butonuna tıklayın.",
                    style: Style(fontSize: 11, italic: true, color: MutedColor, align: "center")),
            }, canGrow: true),
            Band("page-footer", "PageFooter", 10, true, null, new List<object>
            {
                Element("pf-sep", "Shape", 0, 0, 185, 0.3,
                    shapeKind: "Line", style: Style(color: "#cbd5e1")),
                Element("pf-page", "Text", 150, 2, 35, 4,
                    text: "Sayfa {#PageNumber} / {#PageCount}",
                    style: Style(fontSize: 8, color: MutedColor, align: "right")),
            }),
        };
    }

    // ── Reusable bantlar ──────────────────────────────────────────────────────

    private static object BuildStandardTableHeader() => Band("tbl-head", "TableHeader", 8, true, null, new List<object>
    {
        Element("th-bg", "Shape", 0, 0, 185, 8,
            shapeKind: "Rectangle", style: Style(bgColor: AccentColor)),
        Element("th-sira", "Text", 1, 0, 8, 8,
            text: "#", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "center", verticalAlign: "middle")),
        Element("th-kod", "Text", 9, 0, 25, 8,
            text: "Stok Kodu", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "left", verticalAlign: "middle")),
        Element("th-aciklama", "Text", 34, 0, 70, 8,
            text: "Açıklama", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "left", verticalAlign: "middle")),
        Element("th-miktar", "Text", 104, 0, 14, 8,
            text: "Miktar", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "right", verticalAlign: "middle")),
        Element("th-birim", "Text", 118, 0, 12, 8,
            text: "Birim", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "center", verticalAlign: "middle")),
        Element("th-bfiyat", "Text", 130, 0, 18, 8,
            text: "B.Fiyat", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "right", verticalAlign: "middle")),
        Element("th-kdv", "Text", 148, 0, 10, 8,
            text: "KDV%", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "right", verticalAlign: "middle")),
        Element("th-toplam", "Text", 158, 0, 27, 8,
            text: "Toplam", style: Style(fontSize: 9, bold: true, color: "#ffffff", align: "right", verticalAlign: "middle")),
    });

    private static object BuildStandardPageFooter(string alias, string? notesCol)
        => Band("page-footer", "PageFooter", 15, true, null, new List<object>
        {
            Element("pf-sep", "Shape", 0, 0, 185, 0.3,
                shapeKind: "Line", style: Style(color: "#cbd5e1")),
            Element("pf-notes-lbl", "Text", 0, 2, 20, 4,
                text: "Notlar:",
                style: Style(fontSize: 8, bold: true, color: MutedColor)),
            BoundOrPlaceholder("pf-notes", "Text", 20, 2, 130, 8,
                alias, notesCol,
                style: Style(fontSize: 8, color: TextColor)),
            Element("pf-page", "Text", 150, 8, 35, 4,
                text: "Sayfa {#PageNumber} / {#PageCount}",
                style: Style(fontSize: 8, color: MutedColor, align: "right")),
            Element("pf-brand", "Text", 0, 11, 185, 3,
                text: "CalibraHub ile hazırlanmıştır",
                style: Style(fontSize: 7, italic: true, color: "#94a3b8", align: "center")),
        });

    // ── JSON serialization ────────────────────────────────────────────────────

    private static string SerializeLayout(LayoutMeta meta, List<object> bands)
    {
        var doc = new
        {
            pageWidth  = meta.PageW,
            pageHeight = meta.PageH,
            margins    = new
            {
                top    = meta.MarginTop,
                bottom = meta.MarginBot,
                left   = meta.MarginLeft,
                right  = meta.MarginRight,
            },
            bands,
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    // ── Element/Band helpers ──────────────────────────────────────────────────

    private static object Band(
        string id, string type, double height, bool repeat, string? alias,
        List<object> elements,
        bool canGrow = false,
        bool zebraEnabled = false,
        string? zebraColor = null) => new
        {
            id, type, height,
            repeatOnEveryPage = repeat,
            canGrow,
            dataAlias = alias,
            elements,
            zebraEnabled,
            zebraColor,
        };

    private static object Element(
        string id, string kind,
        double x, double y, double w, double h,
        string? text = null,
        string? shapeKind = null,
        string? imageFit = null,
        object? style = null,
        string? barcodeType = null,
        bool? showBarcodeText = null)
    {
        // DocDesigner JSX kind sözlüğü: Label / BoundField / Image / Shape / Barcode /
        // AmountInWords / PageNumber / DateTimeNow. "Text" geçildi ise statik etiket
        // olarak "Label" türüne map ederiz (text alanı dolu).
        var resolvedKind = kind == "Text" ? "Label" : kind;
        return new
        {
            id, kind = resolvedKind, x, y, w, h, text,
            style = style ?? Style(),
            binding = (object?)null,
            format = (string?)null,
            expression = (string?)null,
            shapeKind,
            imageSrc = (string?)null,
            imageFit,
            barcodeType,
            showBarcodeText,
            qrErrorCorrection = (string?)null,
        };
    }

    private static object ElementBound(
        string id, string kind,
        double x, double y, double w, double h,
        string alias, string col,
        string? format = null,
        object? style = null,
        string? barcodeType = null,
        bool? showBarcodeText = null)
    {
        // "Text" parametresi geldiyse, bağlı veri alanı olduğu için BoundField'a map ederiz.
        // Barcode/AmountInWords gibi kind'lar zaten kendi adlarıyla geçer.
        var resolvedKind = kind == "Text" ? "BoundField" : kind;
        return new
        {
            id, kind = resolvedKind, x, y, w, h,
            text = (string?)null,
            style = style ?? Style(),
            binding = new { alias, col },
            format,
            expression = (string?)null,
            shapeKind = (string?)null,
            imageSrc = (string?)null,
            imageFit = (string?)null,
            barcodeType,
            showBarcodeText,
            qrErrorCorrection = (string?)null,
        };
    }

    /// <summary>
    /// Kolon adı varsa bound element, yoksa placeholder text üretir.
    /// View bu alanı tanımlamamışsa kullanıcı manuel bind eder.
    /// </summary>
    private static object BoundOrPlaceholder(
        string id, string kind,
        double x, double y, double w, double h,
        string alias, string? col,
        string? format = null,
        object? style = null)
    {
        if (!string.IsNullOrWhiteSpace(col))
            return ElementBound(id, kind, x, y, w, h, alias, col!, format, style);

        // Kolon yok → açıklayıcı placeholder
        return Element(id, kind, x, y, w, h,
            text: $"[bind: {id}]",
            style: style ?? Style(italic: true, color: "#cbd5e1"));
    }

    private static object Style(
        float fontSize = 10,
        bool bold = false,
        bool italic = false,
        bool underline = false,
        string align = "left",
        string? color = null,
        string? bgColor = null,
        bool border = false,
        string? overflow = null,
        string? verticalAlign = null) => new
        {
            fontSize, bold, italic, underline, align,
            color = color ?? TextColor,
            bgColor, border, overflow, verticalAlign,
        };

    // ── Records ───────────────────────────────────────────────────────────────

    public sealed record LayoutMeta(
        decimal PageW, decimal PageH,
        decimal MarginTop, decimal MarginBot, decimal MarginLeft, decimal MarginRight);

    public sealed record DefaultLayoutResult(LayoutMeta Meta, string LayoutJson, string TemplateKind);
}
