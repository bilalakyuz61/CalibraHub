using CalibraHub.Application.Services.EDocument;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>Bir ERP kaynagindan okunan tek e-belge: ana kayit + detaylari.</summary>
public sealed record OfflineEDocument(IncomingDocument Header, EDocumentDetails Details);

/// <summary>Belge turu basina en son ice aktarilan ERP anahtari (0 = henuz yok).</summary>
public sealed record OfflineSourceWatermark(int LastInvoiceKey, int LastDespatchKey);

/// <summary>
/// CEVRIMDISI e-belge kaynagi: ERP veritabanindan (bugun Netsis) gelen e-fatura /
/// e-arsiv / e-irsaliye kayitlarini okur.
///
/// <para><b>Yapisal veri iliskisel tablolardan okunur</b> (TBLEFATMAS/KALEM/MASTAX,
/// TBLEIRSMAS/KALEM): kalem/vergi kirilimi orada sorgulanabilir haldedir.</para>
///
/// <para><b>Zarf XML'i AYRICA okunur (2026-08-30 duzeltmesi):</b> onceki olcum zarfin
/// <c>XMLVERI</c> kolonuna bakip "1 bayt, pratikte bos" sonucuna varmisti — DOGRU kolon o
/// degil. Gercek UBL, <c>TBLEFATZARF.XMLBYTES</c> icinde ZIP'lenmis durur (arsivdeki
/// <c>efatura_&lt;uuid&gt;.xml</c>) ve olcumde kayitlarin %97.8'inde doludur. Bu XML olmadan
/// ekranda "Resmi (GIB) Goruntusu" (UBL'e gomulu XSLT) ve XML sekmesi URETILEMEZ.</para>
///
/// <para>Bu arayuzun implementasyonlari kaynak veritabanina YALNIZCA okuma amacli baglanir.</para>
/// </summary>
public interface IOfflineEDocumentSource
{
    /// <summary>
    /// Kaynaktan e-belgeleri okur.
    /// </summary>
    /// <param name="connection">Okunacak ERP veritabani baglantisi (salt-okunur).</param>
    /// <param name="since">Bu tarihten (dahil) sonraki belgeler. Tam tarama yerine pencere.</param>
    /// <param name="maxRows">Guvenlik tavani — tek seferde okunacak azami belge sayisi.</param>
    /// <param name="afterSourceKey">
    /// Bu ERP anahtarindan (INCKEYNO) SONRAKI kayitlar okunur — ilerleme isareti.
    /// <para>Gerekli: aksi halde okuyucu her turda EN YENI <c>maxRows</c> kaydi getirir, hepsi
    /// dedup'a takilir ve ESKI belgeler SIRAYA HIC GELMEZ — sistem sessizce ilk tavanda
    /// takili kalirdi (canli dogrulamada gorulen davranis: her tur 0 eklendi / 400 atlandi).</para>
    /// <para>Deger AYRI bir imlec tablosunda tutulmaz; ice aktarilmis kendi verimizden
    /// turetilir, dolayisiyla kayit silinirse kendini onarir ve imlec kaymasi olmaz.</para>
    /// </param>
    Task<IReadOnlyList<OfflineEDocument>> ReadAsync(
        ExternalDbConnection connection,
        DateTime since,
        int maxRows,
        OfflineSourceWatermark afterSourceKey,
        CancellationToken ct);

    /// <summary>
    /// Tek bir belgenin zarf UBL XML'ini okur (ERP anahtarina gore). Bulunamazsa null.
    ///
    /// <para>Daha once ice aktarilmis, XML'siz kayitlar bu yolla tamamlanir — 10 binin
    /// uzerinde belge yeniden aktarilmadan resmi goruntusune kavusur.</para>
    /// </summary>
    /// <param name="sourceKey">Belgenin ERP birincil anahtari (INCKEYNO).</param>
    Task<string?> TryReadEnvelopeXmlAsync(
        ExternalDbConnection connection,
        DocumentKind kind,
        int sourceKey,
        CancellationToken ct);
}
