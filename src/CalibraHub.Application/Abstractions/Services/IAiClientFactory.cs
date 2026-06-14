using CalibraHub.Application.Contracts;
using Microsoft.Extensions.AI;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-05-23 — Provider Code → IChatClient resolve eder.
///
/// **Resolve sırası:**
///   1) Kullanıcı override key varsa (AiUserKey) → o key ile client
///   2) Yoksa şirket default key (AiProvider.ApiKeyEncrypted)
///   3) İkisi de yoksa null (caller hata mesajı gösterir)
///
/// **Per-provider adapter:**
///   - openai      → Microsoft.Extensions.AI.OpenAI (OpenAIClient.AsChatClient)
///   - azure-openai → Microsoft.Extensions.AI.OpenAI + AzureOpenAIClient
///   - anthropic   → AnthropicChatAdapter (custom IChatClient impl, direct HTTP)
///   - gemini      → GeminiChatAdapter (custom IChatClient impl, direct HTTP)
/// </summary>
public interface IAiClientFactory
{
    /// <summary>
    /// providerCode null veya boş ise default provider'a düşer.
    /// userId null ise sadece şirket default key denenir (user override atlanır).
    /// </summary>
    Task<IChatClient?> CreateAsync(string? providerCode, int? userId, CancellationToken ct);

    /// <summary>
    /// Kullanıcıya açık (key dolu olan) provider listesi. Floating widget dropdown'ı buradan dolar.
    /// IsUserOverride flag'i hangi provider'larda kullanıcının override key'i olduğunu gösterir.
    /// </summary>
    Task<IReadOnlyList<AiProviderListItemDto>> ListAvailableAsync(int? userId, CancellationToken ct);
}
