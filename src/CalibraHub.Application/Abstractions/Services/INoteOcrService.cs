namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Not HTML içeriğindeki gömülü resimlerden (base64 data-URI) OCR ile metin çıkarır.
/// Windows.Media.Ocr kullanır; OCR motoru mevcut değilse <c>null</c> döner.
/// </summary>
public interface INoteOcrService
{
    /// <summary>
    /// HTML içindeki &lt;img src="data:image/...;base64,..."&gt; etiketlerini tarayıp
    /// her birinden Windows OCR ile metin çıkarır ve birleştirir.
    /// </summary>
    /// <returns>Birleştirilmiş OCR metni ya da <c>null</c> (görsel yok / çıkarılamadı).</returns>
    Task<string?> ExtractTextFromImagesAsync(string? htmlContent, CancellationToken cancellationToken = default);
}
