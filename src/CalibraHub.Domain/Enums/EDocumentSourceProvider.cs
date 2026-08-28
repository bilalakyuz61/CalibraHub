namespace CalibraHub.Domain.Enums;

/// <summary>
/// E-belgelerin okundugu KAYNAK SAGLAYICI (2026-08-28 kullanici karari).
///
/// <para>Yontemden (Online/Offline) AYRI bir eksendir: hangi yontemin hangi saglayiciya
/// ait oldugu <c>EDocumentSourceCatalog</c>'da tanimlidir. Yeni bir ERP/entegrator eklemek
/// icin buraya bir deger + katalog'a bir satir eklemek yeterlidir.</para>
///
/// <para><b>IntegratorProvider'dan farki:</b> o enum yalnizca CEVRIMICI entegratorleri
/// (Logo/Uyumsoft/EDM) listeler ve IntegratorSetting kaydinin saglayicisidir. Bu enum ise
/// cevrimdisi ERP kaynaklarini da kapsar; sirket parametresinde secilen profildir.</para>
/// </summary>
public enum EDocumentSourceProvider
{
    Unknown = 0,

    /// <summary>Cevrimici entegrator (mevcut entegrator ayarlari bu profile aittir).</summary>
    Logo = 1,

    /// <summary>Cevrimdisi ERP veritabani: TBLEFATZARF / TBLEFATMAS / TBLEIRSMAS.</summary>
    Netsis = 10
}
