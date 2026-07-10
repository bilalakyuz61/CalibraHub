using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalibraHub.Application.Auditing;

/// <summary>
/// Log satırı JSON ayarları — yazıcı ve okuyucu aynı ayarı kullanır.
/// UnsafeRelaxedJsonEscaping: Türkçe karakterler dosyada okunur kalır (\uXXXX değil).
/// Dosyaya yalnızca kendi ürettiğimiz veri yazıldığı için güvenlidir (HTML'e gömülmez).
/// </summary>
public static class AuditJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(AuditEntry entry) => JsonSerializer.Serialize(entry, Options);

    public static AuditEntry? Deserialize(string line)
    {
        try { return JsonSerializer.Deserialize<AuditEntry>(line, Options); }
        catch { return null; } // bozuk satır — okuyucu atlar
    }
}
