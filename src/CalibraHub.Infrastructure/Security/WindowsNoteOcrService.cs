using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Services;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace CalibraHub.Infrastructure.Security;

/// <summary>
/// Windows.Media.Ocr tabanlı not OCR servisi.
/// Not HTML içeriğindeki base64 data-URI resimlerden metin çıkarır.
/// Türkçe dil paketi önceliklidir; yoksa sistem dili / İngilizce kullanılır.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsNoteOcrService : INoteOcrService
{
    // <img ... src="data:image/png;base64,AAAA..." ...>
    private static readonly Regex DataUriRegex = new(
        @"<img[^>]+src=""data:image/[^;]+;base64,([A-Za-z0-9+/=]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    public async Task<string?> ExtractTextFromImagesAsync(
        string? htmlContent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(htmlContent)) return null;

        var matches = DataUriRegex.Matches(htmlContent);
        if (matches.Count == 0) return null;

        // Dil motoru: Türkçe → sistem dili → İngilizce
        var engine = TryGetEngine("tr")
                  ?? OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? TryGetEngine("en");
        if (engine is null) return null;

        var sb = new StringBuilder();
        foreach (Match match in matches)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var base64 = match.Groups[1].Value;
                // Boşluk/satırsonu varsa temizle (bazı editorlar ekler)
                base64 = base64.Replace(" ", "").Replace("\n", "").Replace("\r", "");
                var bytes = Convert.FromBase64String(base64);

                using var stream = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(stream);
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                stream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(stream);
                var bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);

                var result = await engine.RecognizeAsync(bitmap);
                var text = result.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(text);
                }
            }
            catch
            {
                // Tek görsel başarısız olursa diğerlerine devam et; hatayı yutuyoruz
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static OcrEngine? TryGetEngine(string languageTag)
    {
        try { return OcrEngine.TryCreateFromLanguage(new Language(languageTag)); }
        catch { return null; }
    }
}
