namespace CalibraHub.Application.Constants;

/// <summary>
/// Güvenlik modülü şirket parametreleri (formCode = SECURITY).
/// Admin → Parametreler → Güvenlik sekmesinden yönetilir.
/// </summary>
public static class SecurityParameters
{
    public const string FormCode = "SECURITY";

    /// <summary>
    /// Oturum atalet (idle) süresi — dakika. Kullanıcı bu süre boyunca hiçbir işlem
    /// yapmazsa oturumu düşer. 0 = kapalı (idle timeout uygulanmaz). Tanımsız →
    /// <see cref="DefaultSessionIdleMinutes"/>. Client tarafı (Shell) bu değeri okuyup
    /// geri sayımlı uyarı + logout uygular; sunucu backstop'u appsettings
    /// Authentication:IdleMinutes ile ayrıdır (kapalı-tarayıcı senaryosu için).
    /// </summary>
    public const string SessionIdleMinutesKey = "SESSION_IDLE_MINUTES";

    /// <summary>Parametre tanımsızsa kullanılacak varsayılan idle süresi (dk).</summary>
    public const int DefaultSessionIdleMinutes = 30;

    /// <summary>Oturum kapanmadan önce geri sayımlı uyarının gösterileceği süre (saniye).</summary>
    public const int WarningSeconds = 60;
}
