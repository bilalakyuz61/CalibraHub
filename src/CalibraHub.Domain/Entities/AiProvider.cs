using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Şirket bazlı yapay zeka provider yapılandırması — Şirket Ayarları → Yapay Zeka sekmesinden
/// yönetilir. Birden fazla provider aynı anda aktif olabilir (OpenAI + Anthropic + Azure + Gemini
/// birlikte). Kullanıcı sohbet ederken provider seçer; seçmezse IsDefault olan kullanılır.
///
/// **Şifreleme:** ApiKeyEncrypted, IntegratorSecretProtector ile DPAPI şifreli — 'enc:v1:' prefix'i
/// taşır. Repository tarafında write sırasında encrypt + read sırasında decrypt edilir.
///
/// **Hibrit auth:** AiUserKey ile kullanıcı kendi key'iyle override edebilir. Resolve sırası:
/// (1) kullanıcı override → (2) şirket default ApiKey.
/// </summary>
[Description("Şirket bazlı AI provider config (OpenAI / Anthropic / Azure OpenAI / Gemini). Birden fazla aktif olabilir.")]
public sealed class AiProvider
{
    /// <summary>Whitelist — yeni provider eklenince buraya kayıt + adapter yazılır.</summary>
    public static readonly IReadOnlySet<string> AllowedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "openai",
        "anthropic",
        "azure-openai",
        "gemini",
        "ollama",     // 2026-05-23: Lokal LLM runtime — API key opsiyonel.
        "deepseek",   // 2026-05-24: OpenAI-uyumlu API; ucuz; tool calling native.
    };

    public int Id { get; init; }

    /// <summary>Provider kodu — Whitelist: openai / anthropic / azure-openai / gemini.</summary>
    public required string Code { get; set; }

    /// <summary>Kullanıcıya görünen ad ("OpenAI (Şirket Hesabı)"). Liste UI'da Label kullanılır.</summary>
    public required string Label { get; set; }

    /// <summary>
    /// Şirket bazlı API key — IntegratorSecretProtector ile şifreli ('enc:v1:' prefix).
    /// Kullanıcı kendi override key'i (AiUserKey) varsa o tercih edilir.
    /// NULL olabilir (provider tanımlı ama key girilmemiş — listede pasif görünür).
    /// </summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>
    /// Azure OpenAI için zorunlu endpoint URL ('https://my-resource.openai.azure.com/').
    /// OpenAI/Anthropic/Gemini için NULL (SDK default endpoint'i kullanır).
    /// </summary>
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// Default model — 'gpt-4o-mini', 'claude-3-5-sonnet-20241022', 'gemini-1.5-flash' vb.
    /// Kullanıcı request'te model belirtmezse bu kullanılır. NULL ise provider'ın default'u.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Provider-specific ek config JSON. Örnek:
    ///   Azure OpenAI: { "deploymentName": "gpt-4o-deployment", "apiVersion": "2024-08-01-preview" }
    ///   Gemini:       { "safetySettings": [...] }
    /// </summary>
    public string? ExtraJson { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Kullanıcı provider seçmediğinde fallback. Aynı entity tipinde max 1 default.</summary>
    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public DateTime Created { get; init; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public int? CreatedById { get; set; }
    public int? UpdatedById { get; set; }

    /// <summary>
    /// Config tutarlılık kontrolü. Save öncesi servis katmanında çağrılır.
    /// </summary>
    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Code),
            "Provider kodu zorunlu.");
        DomainException.ThrowIf(!AllowedCodes.Contains(Code),
            $"Geçersiz provider kodu: '{Code}'. İzinli: {string.Join(", ", AllowedCodes)}");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Label),
            "Provider etiketi (Label) zorunlu.");

        // Azure OpenAI için endpoint zorunlu
        if (string.Equals(Code, "azure-openai", StringComparison.OrdinalIgnoreCase))
        {
            DomainException.ThrowIf(string.IsNullOrWhiteSpace(EndpointUrl),
                "Azure OpenAI için Endpoint URL zorunlu (ör: https://my-resource.openai.azure.com/).");
        }
    }
}
