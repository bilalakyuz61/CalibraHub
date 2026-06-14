using Azure;
using Azure.AI.OpenAI;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Ai.Providers;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace CalibraHub.Application.Services.Ai;

/// <summary>
/// 2026-05-23 — Provider Code → IChatClient resolver.
///
/// Resolve sırası:
///   1) userId verilmiş VE AiUserKey varsa → kullanıcının kendi key'i
///   2) Aksi halde şirket default key (AiProvider.ApiKeyEncrypted)
///   3) Hiçbiri yoksa null (caller "AI yapılandırılmamış" hatası gösterir)
///
/// Adapter seçimi provider.Code'a göre:
///   openai       → OpenAIClient.AsChatClient (Microsoft.Extensions.AI.OpenAI)
///   azure-openai → AzureOpenAIClient.AsChatClient (Azure.AI.OpenAI + Microsoft.Extensions.AI.OpenAI)
///   anthropic    → AnthropicChatAdapter (custom IChatClient impl)
///   gemini       → GeminiChatAdapter (custom IChatClient impl)
/// </summary>
public sealed class AiClientFactory : IAiClientFactory
{
    private readonly IAiProviderRepository _providerRepo;
    private readonly IAiUserKeyRepository _userKeyRepo;
    private readonly IHttpClientFactory _httpFactory;

    public AiClientFactory(
        IAiProviderRepository providerRepo,
        IAiUserKeyRepository userKeyRepo,
        IHttpClientFactory httpFactory)
    {
        _providerRepo = providerRepo;
        _userKeyRepo  = userKeyRepo;
        _httpFactory  = httpFactory;
    }

    public async Task<IChatClient?> CreateAsync(string? providerCode, int? userId, CancellationToken ct)
    {
        // Provider yükle — code verilmediyse default'a düş
        Domain.Entities.AiProvider? provider;
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            provider = await _providerRepo.GetDefaultAsync(ct).ConfigureAwait(false);
            if (provider is null) return null;
        }
        else
        {
            provider = await _providerRepo.GetByCodeAsync(providerCode.Trim(), ct).ConfigureAwait(false);
            if (provider is null || !provider.IsActive) return null;
        }

        // Key resolve: önce user override, yoksa şirket default
        string? apiKey = null;
        if (userId.HasValue && userId.Value > 0)
            apiKey = await _userKeyRepo.GetDecryptedApiKeyAsync(userId.Value, provider.Id, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(apiKey))
            apiKey = await _providerRepo.GetDecryptedApiKeyAsync(provider.Id, ct).ConfigureAwait(false);

        // 2026-05-23: Ollama lokal — API key opsiyonel. Diger provider'lar icin zorunlu.
        var codeNorm = provider.Code.ToLowerInvariant();
        if (string.IsNullOrEmpty(apiKey) && codeNorm != "ollama") return null;

        // Provider adapter seç
        return codeNorm switch
        {
            "openai"       => CreateOpenAi(apiKey!, provider.DefaultModel),
            "azure-openai" => CreateAzureOpenAi(apiKey!, provider.EndpointUrl, provider.DefaultModel, provider.ExtraJson),
            "anthropic"    => new AnthropicChatAdapter(apiKey!, provider.DefaultModel, _httpFactory.CreateClient("ai-anthropic")),
            "gemini"       => new GeminiChatAdapter(apiKey!, provider.DefaultModel, _httpFactory.CreateClient("ai-gemini")),
            "ollama"       => new OllamaChatAdapter(apiKey, provider.EndpointUrl, provider.DefaultModel, _httpFactory.CreateClient("ai-ollama")),
            // 2026-05-24: DeepSeek — OpenAI-uyumlu API. Mevcut OpenAIClient'i farkli endpoint ile
            // kullanmak yeterli; tool calling native MEAI cevirisiyle calisir.
            "deepseek"     => CreateDeepSeek(apiKey!, provider.EndpointUrl, provider.DefaultModel),
            _              => null,
        };
    }

    public async Task<IReadOnlyList<AiProviderListItemDto>> ListAvailableAsync(int? userId, CancellationToken ct)
    {
        var providers = await _providerRepo.ListAsync(includeInactive: false, ct).ConfigureAwait(false);
        if (providers.Count == 0) return Array.Empty<AiProviderListItemDto>();

        // Kullanıcının override key'leri (varsa) — performance için tek query
        IReadOnlyList<Domain.Entities.AiUserKey> userKeys = Array.Empty<Domain.Entities.AiUserKey>();
        if (userId.HasValue && userId.Value > 0)
            userKeys = await _userKeyRepo.ListByUserAsync(userId.Value, ct).ConfigureAwait(false);
        var userKeyProviderIds = new HashSet<int>(userKeys.Select(k => k.AiProviderId));

        var result = new List<AiProviderListItemDto>();
        foreach (var p in providers)
        {
            // Şirket key'i veya user override'ı var mı? — sadece ulaşılabilir provider'ları listele.
            // 2026-05-23: Ollama key gerektirmez — sadece endpoint dolu olmasi yeterli.
            var hasUserOverride = userKeyProviderIds.Contains(p.Id);
            var hasCompanyKey   = !string.IsNullOrWhiteSpace(p.ApiKeyEncrypted);
            var isOllama        = string.Equals(p.Code, "ollama", StringComparison.OrdinalIgnoreCase);
            if (!hasUserOverride && !hasCompanyKey && !isOllama) continue;

            result.Add(new AiProviderListItemDto(
                Id:             p.Id,
                Code:           p.Code,
                Label:          p.Label,
                DefaultModel:   p.DefaultModel,
                IsDefault:      p.IsDefault,
                IsUserOverride: hasUserOverride));
        }
        return result;
    }

    // ── Adapter create yardımcıları ──────────────────────────────────────

    // 2026-05-24: MEAI 9.5.0-preview API degisikligi —
    //   Eski: OpenAIClient.AsChatClient("model") → IChatClient
    //   Yeni: OpenAIClient.GetChatClient("model").AsIChatClient() → IChatClient
    private static IChatClient CreateOpenAi(string apiKey, string? defaultModel)
    {
        var modelId = string.IsNullOrWhiteSpace(defaultModel) ? "gpt-4o-mini" : defaultModel;
        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        return client.GetChatClient(modelId).AsIChatClient();
    }

    /// <summary>
    /// 2026-05-24 — DeepSeek (OpenAI-uyumlu). OpenAIClient'i custom endpoint ile kullanir.
    /// Default model: deepseek-chat (V3). Diger secenekler: deepseek-reasoner, deepseek-coder.
    /// Endpoint default: https://api.deepseek.com/v1 — provider.EndpointUrl ile override edilebilir.
    /// </summary>
    private static IChatClient CreateDeepSeek(string apiKey, string? endpoint, string? defaultModel)
    {
        var modelId = string.IsNullOrWhiteSpace(defaultModel) ? "deepseek-chat" : defaultModel;
        var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? "https://api.deepseek.com/v1" : endpoint.TrimEnd('/');
        var opts = new OpenAI.OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl),
        };
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), opts);
        var inner = client.GetChatClient(modelId).AsIChatClient();
        // 2026-05-24: DeepSeek text-only — resim eklenirse HTTP 400 ('image_url' unknown variant)
        // doner. Wrapper resim bloklarini siyirip kullaniciya bilgi notu ekler.
        return new Providers.TextOnlyChatClientWrapper(inner);
    }

    private static IChatClient CreateAzureOpenAi(string apiKey, string? endpoint, string? defaultModel, string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Azure OpenAI provider için EndpointUrl zorunlu.");
        // Azure'da "model" yerine deploymentName kullanılır. ExtraJson'da
        // {"deploymentName": "..."} varsa onu kullan, yoksa DefaultModel'i deployment kabul et.
        var deploymentName = string.IsNullOrWhiteSpace(defaultModel) ? "gpt-4o" : defaultModel;
        if (!string.IsNullOrWhiteSpace(extraJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(extraJson);
                if (doc.RootElement.TryGetProperty("deploymentName", out var dn) && dn.ValueKind == System.Text.Json.JsonValueKind.String)
                    deploymentName = dn.GetString()!;
            }
            catch { /* malformed extraJson — defaultModel kullanılır */ }
        }

        var azClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
        return azClient.GetChatClient(deploymentName).AsIChatClient();
    }
}
