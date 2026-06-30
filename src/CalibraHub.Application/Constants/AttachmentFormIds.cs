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
}
