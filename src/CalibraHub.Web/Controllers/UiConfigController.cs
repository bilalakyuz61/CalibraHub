using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Kullanıcı bazlı basit UI tercihleri — şema gerektirmeyen anahtar/değer ayarları
/// (IUserSettingRepository / UserSettings tablosu). 2026-08-19: belge kalem listesi
/// görünüm modu (KART/DATAGRID) — bkz. CalibraLineItemsGrid frontend sözleşmesi.
/// 2026-08-27 (PageComment Seq 1123): açık workspace sekmeleri — Shell.jsx'in
/// yalnız localStorage'da tuttuğu <c>tabs</c> state'inin sunucu yansıması.
/// </summary>
[Authorize]
public sealed class UiConfigController : Controller
{
    private readonly IUserSettingRepository _userSettings;
    private readonly ILogger<UiConfigController> _logger;

    public UiConfigController(IUserSettingRepository userSettings, ILogger<UiConfigController> logger)
    {
        _userSettings = userSettings;
        _logger = logger;
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;

    public sealed record LineViewModeRequest(string? Mode);

    /// <summary>
    /// Belge kalem listesi görünüm modunu (KART/DATAGRID) kaydeder — kullanıcı bazlı.
    /// Geçersiz mode değeri REDDEDİLİR (sessizce "card"a düşürülmez) — istemci kendi
    /// tarafında zaten yalnız "card"/"grid" gönderir; beklenmeyen bir değer istemci
    /// hatasına işaret eder ve kullanıcıya bildirilmelidir.
    /// </summary>
    [HttpPost("/UiConfig/LineViewMode")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("Izin gerekmez: yalniz cagiranin kendi gorunum tercihi (userId ile yazilir)")]
    public async Task<IActionResult> LineViewMode([FromBody] LineViewModeRequest? request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Json(new { ok = false, error = "Oturum kullanıcısı çözümlenemedi." });

        var mode = request?.Mode?.Trim();
        if (!UiConfigKeys.IsValidViewMode(mode))
            return Json(new { ok = false, error = "Geçersiz görünüm modu. 'card' veya 'grid' olmalı." });

        mode = UiConfigKeys.NormalizeViewMode(mode);

        try
        {
            await _userSettings.SetAsync(userId.Value, UiConfigKeys.LineGridViewMode, mode, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kalem görünüm modu kaydedilemedi (userId={UserId}, mode={Mode})", userId, mode);
            return Json(new { ok = false, error = "Görünüm modu kaydedilemedi. Ayrıntılar sunucu loglarında." });
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Workspace sekmeleri (PageComment Seq 1123, 2026-08-27)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// İstemciden kabul edilen sekme alanları — Shell.jsx'in `tabs` state'inde
    /// gerçekten kullandığı alanlarla BİREBİR sınırlıdır (key/url/title/parentKey).
    /// İstemciden gelen nesne olduğu gibi saklanmaz — beyaz liste zorunlu.
    /// </summary>
    public sealed record WorkspaceTabDto(string? Key, string? Url, string? Title, string? ParentKey);

    public sealed record WorkspaceTabsSaveRequest(List<WorkspaceTabDto>? Tabs);

    private const int MaxWorkspaceTabCount = 30;
    private const int MaxTabKeyLength = 100;
    private const int MaxTabUrlLength = 2000;
    private const int MaxTabTitleLength = 200;
    // Alan bazlı üst sınırların (30 sekme × ~2300 karakter/sekme) üzerinde ekstra bir
    // toplam-boyut kemeri — tek bir bozuk/aşırı büyük payload'ın DB'ye yazılmasını
    // önler (kolonun kendisi NVARCHAR(MAX), bu yüzden DB seviyesinde doğal bir sınır yok).
    private const int MaxWorkspaceTabsJsonLength = 100_000;

    /// <summary>
    /// GET /UiConfig/WorkspaceTabs — kullanıcının (dolayısıyla şirketin; her şirkette
    /// ayrı bir kullanıcı kaydı olduğundan userId zaten şirket bazında ayrışır) kayıtlı
    /// açık sekmelerini döner. "saved:false" (hiç kayıt yok / ilk giriş) ile
    /// "saved:true, tabs:[]" (kullanıcı tüm sekmeleri kapatmış) AYRI durumlardır —
    /// Shell.jsx bu ayrımla ilk-ziyaret varsayılan sekmesini mi açacağına yoksa boş
    /// ekranı mı göstereceğine karar verir; birleştirilirse tam yenilemede hayalet
    /// sekme açılır.
    /// </summary>
    [HttpGet("/UiConfig/WorkspaceTabs")]
    public async Task<IActionResult> GetWorkspaceTabs(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Json(new { ok = false, error = "Oturum kullanıcısı çözümlenemedi." });

        try
        {
            var raw = await _userSettings.GetAsync(userId.Value, UiConfigKeys.WorkspaceTabs, ct);
            if (raw is null)
                return Json(new { ok = true, saved = false });

            List<WorkspaceTabDto>? tabs;
            try
            {
                tabs = JsonSerializer.Deserialize<List<WorkspaceTabDto>>(raw);
            }
            catch (JsonException)
            {
                // Bozuk kayıt (elle DB müdahalesi vb.) — "kayıt var ama boş" olarak
                // fail-open davran; kullanıcının tüm sekmelerini kaybetmiş gibi
                // gösterme ama uygulamayı da çökertme.
                tabs = new List<WorkspaceTabDto>();
            }

            return Json(new { ok = true, saved = true, tabs = tabs ?? new List<WorkspaceTabDto>() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workspace sekmeleri okunamadı (userId={UserId})", userId);
            return Json(new { ok = false, error = "Sekmeler yüklenemedi. Ayrıntılar sunucu loglarında." });
        }
    }

    /// <summary>
    /// POST /UiConfig/WorkspaceTabs — kullanıcının açık sekme listesini kalıcılaştırır.
    /// Gövdedeki her alan doğrulanır/kırpılır; şüpheli bir tab'ı SESSİZCE atmak yerine
    /// tüm istek net bir hata ile reddedilir (kısmi/tutarsız kayıt yazılmaz).
    /// </summary>
    [HttpPost("/UiConfig/WorkspaceTabs")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("Izin gerekmez: yalniz cagiranin kendi acik sekme listesi (userId ile yazilir)")]
    public async Task<IActionResult> SaveWorkspaceTabs([FromBody] WorkspaceTabsSaveRequest? request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Json(new { ok = false, error = "Oturum kullanıcısı çözümlenemedi." });

        var tabs = request?.Tabs ?? new List<WorkspaceTabDto>();
        if (tabs.Count > MaxWorkspaceTabCount)
            return Json(new { ok = false, error = $"En fazla {MaxWorkspaceTabCount} sekme saklanabilir (gönderilen: {tabs.Count})." });

        var sanitized = new List<object>(tabs.Count);
        foreach (var tab in tabs)
        {
            var key = tab.Key?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > MaxTabKeyLength)
                return Json(new { ok = false, error = "Geçersiz sekme anahtarı (key)." });

            var url = tab.Url?.Trim();
            if (string.IsNullOrEmpty(url) || url.Length > MaxTabUrlLength || !IsSafeRelativeUrl(url))
                return Json(new { ok = false, error = $"Geçersiz sekme adresi: yalnız uygulama içi göreli yol ('/' ile başlayan) kabul edilir." });

            var title = tab.Title?.Trim();
            if (!string.IsNullOrEmpty(title) && title.Length > MaxTabTitleLength)
                title = title.Substring(0, MaxTabTitleLength);

            // Aşırı uzun/bozuk parentKey sessizce top-level'a düşürülür (Shell.jsx'teki
            // capTabsAtLimit "orphan promote" davranışıyla tutarlı) — tüm isteği reddetmez.
            var parentKey = tab.ParentKey?.Trim();
            if (!string.IsNullOrEmpty(parentKey) && parentKey.Length > MaxTabKeyLength)
                parentKey = null;

            sanitized.Add(new
            {
                key,
                url,
                title = string.IsNullOrEmpty(title) ? null : title,
                parentKey = string.IsNullOrEmpty(parentKey) ? null : parentKey,
            });
        }

        var json = JsonSerializer.Serialize(sanitized);
        if (json.Length > MaxWorkspaceTabsJsonLength)
            return Json(new { ok = false, error = "Sekme verisi izin verilen boyutu aşıyor." });

        try
        {
            await _userSettings.SetAsync(userId.Value, UiConfigKeys.WorkspaceTabs, json, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workspace sekmeleri kaydedilemedi (userId={UserId})", userId);
            return Json(new { ok = false, error = "Sekmeler kaydedilemedi. Ayrıntılar sunucu loglarında." });
        }
    }

    /// <summary>
    /// Yalnız uygulama içi göreli yol kabul eder ("/..." ile başlar). Protokol-göreli
    /// ("//evil.com"), mutlak ("http://...") ve ters slash tabanlı kaçış ("\evil.com" —
    /// bazı tarayıcılar backslash'i forward slash'e normalize eder) değerler reddedilir.
    /// </summary>
    private static bool IsSafeRelativeUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url[0] != '/') return false;
        if (url.Length > 1 && url[1] == '/') return false; // protokol-göreli
        if (url.IndexOf('\\') >= 0) return false; // backslash kaçışı
        return true;
    }
}
