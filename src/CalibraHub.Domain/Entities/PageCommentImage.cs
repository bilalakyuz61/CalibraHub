namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir <see cref="PageComment"/>'e eklenen görsel (ör. ekran görüntüsü).
/// <see cref="Bytes"/> yalnızca ikili okuma sırasında doldurulur — liste/metadata sorgularında
/// bellek/ağ maliyetinden kaçınmak için null bırakılır. Id istemci tarafında üretilir (GUID string).
/// CommentId FK CASCADE — yorum silinince görseller de gider.
/// </summary>
public sealed class PageCommentImage
{
    public string Id { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string? Mime { get; set; }
    public string? Name { get; set; }

    /// <summary>Yalnızca <c>GetImageBinaryAsync</c> ile doldurulur; metadata sorgularında null.</summary>
    public byte[]? Bytes { get; set; }

    public string? ClientCreatedAt { get; set; }
    public DateTime Created { get; set; }
}
