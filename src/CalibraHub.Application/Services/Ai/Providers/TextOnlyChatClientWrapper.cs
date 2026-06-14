using Microsoft.Extensions.AI;

namespace CalibraHub.Application.Services.Ai.Providers;

/// <summary>
/// 2026-05-24 — Text-only model'ler icin IChatClient sarmalayicisi.
/// DeepSeek ve text-only Ollama modelleri (llama3.1:8b vs.) resim icerigini kabul etmez —
/// API "image_url" alanini reddeder (HTTP 400). Bu wrapper mesajlardan DataContent (resim)
/// bloklarini cikarir, yerlerine kullaniciya bilgilendirici metin koyar; sonra inner client'a
/// yontlendirir.
///
/// Kullanim: AiClientFactory.CreateDeepSeek() / CreateOllama() (vision-only modeller haric)
/// sarmalanmis client doner.
/// </summary>
public sealed class TextOnlyChatClientWrapper : IChatClient
{
    private readonly IChatClient _inner;

    public TextOnlyChatClientWrapper(IChatClient inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => _inner.GetService(serviceType, serviceKey);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetResponseAsync(StripImages(messages), options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetStreamingResponseAsync(StripImages(messages), options, cancellationToken);

    public void Dispose() => _inner.Dispose();

    /// <summary>
    /// Mesaj listesinde DataContent (image/*) bloklarini bul, kaldir, yerine "[Resim eklendi ama
    /// bu model resim okuyamiyor — sadece metin uzerinden cevap verebilir]" notu koy.
    /// Text, FunctionCallContent, FunctionResultContent korunur.
    /// </summary>
    private static IEnumerable<ChatMessage> StripImages(IEnumerable<ChatMessage> messages)
    {
        foreach (var m in messages)
        {
            if (m.Contents == null || m.Contents.Count == 0)
            {
                yield return m;
                continue;
            }

            int imageCount = 0;
            foreach (var c in m.Contents)
            {
                if (c is DataContent dc && (dc.MediaType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    imageCount++;
            }

            if (imageCount == 0)
            {
                yield return m;
                continue;
            }

            // Yeni icerik listesi: image bloklarini kaldir, kullaniciya bilgi notu ekle
            var newContents = new List<AIContent>(m.Contents.Count);
            foreach (var c in m.Contents)
            {
                if (c is DataContent dc && (dc.MediaType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    continue;   // skip image
                newContents.Add(c);
            }
            newContents.Add(new TextContent(
                $"\n[Not: Kullanici bu mesaja {imageCount} resim ekledi ancak aktif AI modeli resim okuyamiyor (text-only). Cevabini sadece metin uzerinden ver; resim icerigi hakkinda yorum yapma. Kullaniciya 'Bu modelde resim destegi yok, multimodal model (Claude/Gemini/GPT-4o) kullanmanizi onerirsem' diyebilirsin.]"));

            yield return new ChatMessage(m.Role, newContents);
        }
    }
}
