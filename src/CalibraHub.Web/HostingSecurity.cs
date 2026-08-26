using Microsoft.AspNetCore.Http;

namespace CalibraHub.Web;

/// <summary>
/// Barindirma (hosting) guvenlik kararlarinin TEK noktasi.
///
/// <para><b>Neden (2026-08-24 guvenlik denetimi, ORTA):</b> oturum cerezleri
/// <c>CookieSecurePolicy.SameAsRequest</c> ile kuruluyordu — yani http uzerinden
/// gelen tek bir istekte cerez de duz http'de gonderiliyordu. HTTPS zorlanan bir
/// kurulumda bu gereksiz bir sizinti yuzeyi. Karar Program.cs'te uc ayri yerde
/// tekrarlandigi icin (DRY) burada tek fonksiyona indirildi.</para>
/// </summary>
public static class HostingSecurity
{
    /// <summary>
    /// <c>Hosting:RequireHttps</c> acikca <c>true</c> ise cerezler yalnizca https'te
    /// gonderilir; aksi halde mevcut davranis (istekle ayni) korunur.
    /// Varsayilanin degismemesi bilincli: yerelde http ile calisan gelistirme ve
    /// http dinleyen mevcut kurulumlar bu degisiklikten etkilenmemeli.
    /// </summary>
    public static CookieSecurePolicy CookiePolicy(IConfiguration configuration)
        => configuration.GetValue<bool?>("Hosting:RequireHttps") == true
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
}
