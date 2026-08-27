namespace CalibraHub.Domain.Enums;

/// <summary>
/// Gelen e-belgenin sisteme HANGI YOLDAN girdigi (2026-08-28 kullanici karari).
/// Iki yol da AYNI hedefe (IncomingDocument ailesi) yazar; ayrim tesbis ve raporlama icindir.
/// </summary>
public enum EDocumentIngestSource
{
    /// <summary>Entegrator API'si (Logo vb.) uzerinden cevrimici cekildi.</summary>
    Online = 0,

    /// <summary>ERP veritabanindan (TBLEFATZARF/TBLEFATMAS/TBLEIRSMAS...) cevrimdisi aktarildi.
    /// Bu yolda entegrator YOKTUR — IntegratorSettingsId null kalir.</summary>
    Offline = 1
}
