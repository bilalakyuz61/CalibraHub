using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Constants;

/// <summary>
/// E-Belge alma yontemi ve kaynak profili — sirket parametreleri
/// (Admin -> Parametreler -> E-Belge sekmesi, 2026-08-28 kullanici karari).
///
/// <para><b>Iki kademeli secim:</b> once YONTEM (Online / Offline), sonra o yonteme ait
/// SAGLAYICI secilir. Online = entegrator API'si (bugun Logo), Offline = ERP veritabani
/// (bugun Netsis: TBLEFATZARF / TBLEFATMAS / TBLEIRSMAS). Ikisi de AYNI hedefe
/// (IncomingDocument ailesi) yazar; ayrim yalnizca OKUMA kaynagindadir.</para>
///
/// <para><b>Neden profil altyapisi:</b> ileride baska bir ERP (online ya da offline) eklenmesi
/// isteniyor. Saglayici listesi <see cref="EDocumentSourceCatalog"/>'da tanimlidir; yeni bir
/// kaynak eklemek icin enum'a bir deger ve katalog'a bir satir eklemek yeterlidir — parametre
/// semasi, ekran ve dogrulama degismez.</para>
/// </summary>
public static class EDocumentParameters
{
    /// <summary>Parametrelerin saklandigi form kodu → Parametreler ekranindaki "E-Belge" sekmesi.</summary>
    public const string FormCode = "EDOCUMENT";

    /// <summary>Alma yontemi: <c>Online</c> | <c>Offline</c> (<see cref="EDocumentIngestSource"/>).</summary>
    public const string IngestMethodKey = "EDOC_INGEST_METHOD";

    /// <summary>Secili kaynak saglayici: <c>Logo</c> | <c>Netsis</c> ... (<see cref="EDocumentSourceProvider"/>).</summary>
    public const string IngestProviderKey = "EDOC_INGEST_PROVIDER";

    public const EDocumentIngestSource DefaultMethod = EDocumentIngestSource.Online;
    public const EDocumentSourceProvider DefaultProvider = EDocumentSourceProvider.Logo;
}

/// <summary>Bir e-belge kaynak profilinin tanimi (yontem + saglayici + ekran etiketi).</summary>
public sealed record EDocumentSourceProfile(
    EDocumentSourceProvider Provider,
    EDocumentIngestSource Method,
    string Label,
    string Description);

/// <summary>
/// Tanimli e-belge kaynak profilleri. TEK kayit yeri: ekran listeyi buradan doldurur,
/// sunucu dogrulamasi da buradan yapar — ikisi ayrisamaz.
/// </summary>
public static class EDocumentSourceCatalog
{
    public static readonly IReadOnlyList<EDocumentSourceProfile> Profiles = new[]
    {
        new EDocumentSourceProfile(
            EDocumentSourceProvider.Logo,
            EDocumentIngestSource.Online,
            "Logo",
            "Entegrator API'sinden cevrimici cekim (mevcut entegrator ayarlari bu profile aittir)."),

        new EDocumentSourceProfile(
            EDocumentSourceProvider.Netsis,
            EDocumentIngestSource.Offline,
            "Netsis",
            "ERP veritabanindan cevrimdisi aktarim (TBLEFATZARF / TBLEFATMAS / TBLEIRSMAS)."),
    };

    public static IEnumerable<EDocumentSourceProfile> ForMethod(EDocumentIngestSource method) =>
        Profiles.Where(p => p.Method == method);

    /// <summary>
    /// Saglayici verilen yonteme ait mi? Ekran listeyi zaten filtreler, ama SUNUCU de
    /// dogrular: istemciden gelen (Offline + Logo) gibi tutarsiz bir cift sessizce
    /// kaydedilirse ice aktarim calisma zamaninda yanlis kaynaktan okumaya calisirdi.
    /// </summary>
    public static bool IsValid(EDocumentIngestSource method, EDocumentSourceProvider provider) =>
        Profiles.Any(p => p.Method == method && p.Provider == provider);

    /// <summary>Yonteme ait ILK saglayici — yontem degisince makul varsayilan.</summary>
    public static EDocumentSourceProvider DefaultFor(EDocumentIngestSource method) =>
        ForMethod(method).Select(p => p.Provider).FirstOrDefault();
}
