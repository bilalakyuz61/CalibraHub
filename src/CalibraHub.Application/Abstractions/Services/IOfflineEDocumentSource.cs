using CalibraHub.Application.Services.EDocument;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>Bir ERP kaynagindan okunan tek e-belge: ana kayit + detaylari.</summary>
public sealed record OfflineEDocument(IncomingDocument Header, EDocumentDetails Details);

/// <summary>Belge turu basina en son ice aktarilan ERP anahtari (0 = henuz yok).</summary>
public sealed record OfflineSourceWatermark(int LastInvoiceKey, int LastDespatchKey);

/// <summary>
/// CEVRIMDISI e-belge kaynagi: ERP veritabanindan (bugun Netsis) gelen e-fatura /
/// e-arsiv / e-irsaliye kayitlarini okur.
///
/// <para><b>Neden XML degil iliskisel okuma:</b> ilk tasarimda zarf tablosundaki ham UBL
/// (<c>TBLEFATZARF.XMLVERI</c>) okunup mevcut ayristiriciya verilecekti. Olcum bunu
/// curuttu: 14.382 zarf satirinin TAMAMINDA bu kolon 1 bayt — yani pratikte BOS. Veri
/// iliskisel tablolarda (TBLEFATMAS/KALEM/MASTAX, TBLEIRSMAS/KALEM) duruyor, dolayisiyla
/// kaynak onlardan okunur ve dogrudan yapisal veri uretir.</para>
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
}
