using System;

namespace CalibraHub.Web.Models.Shared;

/// <summary>
/// Tum belge (Satis Teklifi, Siparis, Fatura, Irsaliye vb.) tasarim/edit
/// sayfalarinda kullanilabilen ortak view modeli.
///
/// 4 bolum icerir:
///   1) Ust bilgiler  — belge no, tarih, cari, temsilci, para birimi, durum
///   2) Kalem bilgileri — CalibraLineItemsGrid React bileseni + JSON config
///   3) Ek alanlar      — DynamicWidgetRenderer header + line widget'lari
///   4) Kalibrasyon/Kurumsal — Company entity'sinden turetilen belge ustu kimlik
///
/// Kullanim: Controller icinde bu VM'i doldur ve
/// &lt;partial name="_SharedDocumentDesign" model="@vm" /&gt; olarak render et.
/// Tek cshtml iki+ belge turune hizmet eder — sadece FormCode ve DocumentTypeCode
/// degisir.
/// </summary>
public sealed class SharedDocumentDesignViewModel
{
    // ── 1) UST BILGILER ──────────────────────────────────────────

    /// <summary>Belge PK (mevcut kayit duzenleniyorsa).</summary>
    public int? DocumentId { get; set; }

    /// <summary>Belge tipi kodu (QUOTE, ORDER, INVOICE, DISPATCH, ...).</summary>
    public string DocumentTypeCode { get; set; } = "QUOTE";

    /// <summary>Ust basligi — browser title ve header'da kullanilir.</summary>
    public string DocumentLabel { get; set; } = "Belge";

    /// <summary>Belge numarasi (taslakta bos, kayit sonrasi uretilir).</summary>
    public string? DocumentNumber { get; set; }

    /// <summary>Belge tarihi (varsayilan bugun).</summary>
    public DateTime DocumentDate { get; set; } = DateTime.Today;

    /// <summary>Ikinci tarih alani — gecerlilik / termin / vade.</summary>
    public DateTime? SecondaryDate { get; set; }

    /// <summary>Ikinci tarih etiketi — "Gecerlilik", "Vade Tarihi", vs.</summary>
    public string SecondaryDateLabel { get; set; } = "Gecerlilik";

    /// <summary>Para birimi (TRY/USD/EUR).</summary>
    public string Currency { get; set; } = "TRY";

    /// <summary>Durum (Draft, Sent, Approved, ...).</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>Cari referansi.</summary>
    public ContactReference? Contact { get; set; }

    /// <summary>Satis temsilcisi ID.</summary>
    public int? SalesRepId { get; set; }

    // ── 2) KALEM BILGILERI ───────────────────────────────────────

    /// <summary>
    /// CalibraLineItemsGrid icin onceden serialize edilmis config JSON'u.
    /// Controller tarafinda BuildDocumentLineGridConfig(bindings) ile uretilir.
    /// Shema: { schemaVersion, columns:[...], rows:[], labels, footer }
    /// </summary>
    public string LineGridConfigJson { get; set; } = "{}";

    // ── 3) EK ALANLAR (WIDGET) ───────────────────────────────────

    /// <summary>
    /// Ust bilgi widget'lari icin form kodu (SALES_QUOTE_EDIT, SALES_ORDER_EDIT...).
    /// Bos ise ek alanlar sekmesi gizlenir.
    /// </summary>
    public string? HeaderFormCode { get; set; } = "SALES_QUOTE_EDIT";

    /// <summary>
    /// Satir bazli widget'lar icin form kodu (SALES_QUOTE_LINES, SALES_ORDER_LINES...).
    /// Bos ise line grid'de ⚙ butonu devre disi.
    /// </summary>
    public string? LineFormCode { get; set; } = "SALES_QUOTE_LINES";

    // ── 4) KALIBRASYON / KURUMSAL ────────────────────────────────

    /// <summary>
    /// Belge ustunde basilacak sirket kimlik bilgileri. Null ise kalibrasyon
    /// bolumu gizlenir.
    /// </summary>
    public CompanyCalibrationDto? Calibration { get; set; }
}

/// <summary>
/// Belge icindeki cari referansi (sadece display). Rehberden secim sonrasi
/// doldurulur.
/// </summary>
public sealed class ContactReference
{
    public int? Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// Belge ustunde basilan sirket/kalibrasyon bilgileri. Domain'deki Company
/// entity'sinden projeksiyondur.
/// </summary>
public sealed class CompanyCalibrationDto
{
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? District { get; set; }
    public string? PostalCode { get; set; }
    public string TaxOffice { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    /// <summary>Opsiyonel — sirket logosu static path (ornek /img/logo.svg).</summary>
    public string? LogoUrl { get; set; }
    /// <summary>E-Belge (e-Fatura/e-Irsaliye) onay akisi aktif mi.</summary>
    public bool IsEDocumentEnabled { get; set; }
}
