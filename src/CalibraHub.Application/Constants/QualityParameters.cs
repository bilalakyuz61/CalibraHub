namespace CalibraHub.Application.Constants;

/// <summary>
/// Kalite modülü şirket parametreleri (formCode = QUALITY).
/// Admin → Parametreler → Kalite sekmesinden yönetilir.
/// Ayrı bir form kodu olarak açıldı (2026-08-26, PageComment Seq 1119): mevcut gruplardan
/// hiçbiri (SECURITY/STOCK/PRODUCTION/...) Kalite/DÖF alanına ait değildi; QUALITY_CAPA
/// (FormCodes.QualityCapa) ise ekran yetki kodu, parametre saklamak için kullanılmaz.
/// </summary>
public static class QualityParameters
{
    public const string FormCode = "QUALITY";

    /// <summary>
    /// Cari kartı üzerinden DÖF açma desteklensin mi (Bool). Kapalıyken (varsayılan):
    /// (1) Cari liste ekranının satır İşlemler menüsünde "DÖF Aç" aksiyonu HİÇ render edilmez,
    /// (2) sourceKind="Contact" ile DÖF kaydetme isteği sunucu tarafında da reddedilir
    /// (CapaService.SaveAsync) — yalnızca UI gizleme değil, gerçek kapı. Tanımsız → KAPALI
    /// (kullanıcı özelliği açıkça bu parametreye bağlamak istedi; sessiz varsayılan-açık YOK).
    /// </summary>
    public const string ContactCapaEnabledKey = "CONTACT_CAPA_ENABLED";

    public const bool DefaultContactCapaEnabled = false;
}
