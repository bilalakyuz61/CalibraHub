using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalibraHub.Application.Diagnostics;

/// <summary>
/// Hata log satırı JSON ayarları — <see cref="Application.Auditing.AuditJson"/> ile aynı desen.
/// UnsafeRelaxedJsonEscaping: Türkçe karakterler dosyada okunur kalır. Dosyaya yalnızca
/// kendi ürettiğimiz veri yazıldığı için güvenlidir (HTML'e gömülmez).
/// </summary>
public static class SystemErrorLogJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(SystemErrorEntry entry) => JsonSerializer.Serialize(entry, Options);

    public static SystemErrorEntry? Deserialize(string line)
    {
        try { return JsonSerializer.Deserialize<SystemErrorEntry>(line, Options); }
        catch { return null; } // bozuk satır — okuyucu atlar
    }
}
