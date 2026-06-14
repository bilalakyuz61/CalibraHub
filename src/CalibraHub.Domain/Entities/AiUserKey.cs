using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Kullanıcının bir AI provider için kendi override API key'i. Profil sayfası "AI Anahtarlarım"
/// bölümünden yönetilir. Şirket bazlı default key (AiProvider.ApiKeyEncrypted) yerine bunun
/// kullanılması istendiğinde tanımlanır.
///
/// **Resolve sırası (AiClientFactory):**
///   1) AiUserKey (UserId, AiProviderId) varsa → bunun ApiKeyEncrypted'ı kullanılır
///   2) Yoksa → AiProvider.ApiKeyEncrypted (şirket default)
///   3) İkisi de yoksa → IChatClient null döner (kullanıcıya hata gösterilir)
///
/// **CASCADE:** Provider silinince user override'lar da silinir (FK_AiUserKey_Provider).
/// </summary>
[Description("Kullanıcının AI provider için override API key'i (şirket default'u yerine).")]
public sealed class AiUserKey
{
    public int Id { get; init; }

    public int UserId { get; set; }

    public int AiProviderId { get; set; }

    /// <summary>IntegratorSecretProtector ile şifreli ('enc:v1:' prefix).</summary>
    public required string ApiKeyEncrypted { get; set; }

    public DateTime Created { get; init; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(UserId <= 0,
            "UserId zorunlu.");
        DomainException.ThrowIf(AiProviderId <= 0,
            "AiProviderId zorunlu.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(ApiKeyEncrypted),
            "ApiKey zorunlu.");
    }
}
