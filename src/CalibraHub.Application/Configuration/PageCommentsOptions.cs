namespace CalibraHub.Application.Configuration;

/// <summary>
/// Sayfa-içi Yorum sisteminin Katman-4 (otonom ajan döngüsü) erişim ayarları.
/// appsettings "PageComments" bölümünden Program.cs'de doldurulur.
///
/// <see cref="AgentAccessEnabled"/> yalnızca <c>IWebHostEnvironment.IsDevelopment()</c> false
/// olan (üretim) ortamlarda anlamlıdır — Development'ta gate zaten otomatik açıktır.
/// Üretimde bilinçli olarak açılmadıkça (true) agent-queue/agent-apply endpoint'leri 404 döner.
/// </summary>
public sealed class PageCommentsOptions
{
    public bool AgentAccessEnabled { get; init; }

    /// <summary><c>X-Agent-Key</c> header'ı ile birebir (sabit-zamanlı) karşılaştırılan paylaşılan sır.</summary>
    public string AgentKey { get; init; } = string.Empty;
}
