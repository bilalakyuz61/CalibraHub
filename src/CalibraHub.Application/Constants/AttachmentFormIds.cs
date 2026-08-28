namespace CalibraHub.Application.Constants;

/// <summary>
/// dbo.Attachment tablosundaki FormId sütunu için sabit değerler.
/// EntityType (string) yerine INT kullanılır — ID tabanlı eşleştirme kuralı.
/// </summary>
public static class AttachmentFormIds
{
    public const int DocMgr          = 1;  // Serbest belgeler (modüle bağlı olmayan)
    public const int Asset           = 2;  // Varlık belgeleri
    public const int AssetImage      = 3;  // Varlık kapak görseli
    public const int AssetAssignment = 4;  // Zimmet imzası / belgesi
    public const int WidgetAttachment = 5; // EAV widget 'attachment' tipi — RefId = WidgetMas.Id, kayit bagi WidgetTra.Value (attachment Id)

    /// <summary>
    /// Kalite muayenesi foto/kanit eki — RefId = muayenenin <c>Document.Id</c>'si
    /// (QualityInspection.Id DEGIL; muayene Document'a 1-1 companion olarak baglidir,
    /// tum servis/uc imzalari DocumentId ile calisir).
    ///
    /// NEDEN mevcut DocumentAttachment kullanilmadi: o ayri bir tablo (company DB, ham SQL) ve
    /// [PermissionScope(DocTemplates)] ile korunuyor — muayene yetkisi olan kullanici erisemez.
    /// Merkezi Attachment tablosu revizyon destegi de getirir.
    /// </summary>
    public const int QualityInspection = 6;
}
