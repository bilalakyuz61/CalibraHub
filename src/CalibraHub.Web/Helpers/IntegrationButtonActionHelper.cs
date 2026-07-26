using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services.Security;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Web.Helpers;

/// <summary>
/// SmartBoard satır "İşlemler" menüsüne, bir FormCode altında tanımlı AKTİF + Manual
/// tetikleyicili entegrasyon butonlarını (ör. "ERP'ye Aktar") ekleyen yardımcı
/// (AuditLogActionHelper deseninde — 2026-07-21).
///
/// Bugüne kadar bu butonlar yalnızca FORM (edit) ekranında görünüyordu
/// (bkz. Views/Shared/_IntegrationButtons.cshtml — "Şimdi Çalıştır" standalone buton,
/// onay modalı YOK, doğrudan tetikler). Bu helper AYNI tetikleme ucunu
/// (POST /Integration/Run/{integrationId}?recordId=...) LİSTE ekranı satır menüsüne taşır.
/// Kasıtlı olarak onay modalı eklenmez — form ekranındaki referans buton da doğrudan
/// tetikliyor, davranış eşleşir (_IntegrationActionItems.cshtml'deki dropdown varyantında
/// olan CalibraAlert.confirm adımı BURADA YOK — o ayrı bir ekran/akış).
///
/// İki adımlı kullanım (board request başına N+1 sorgu/izin kontrolünü önlemek için):
///   1) Board request başına BİR KEZ: GetAuthorizedManualButtonsAsync(formCode, ...) — aktif
///      Manual entegrasyonları listeler ve BUTTON:INT_{id} PermissionDef'i olan her biri için
///      kullanıcı yetkisini kontrol eder (yetkisiz olanlar sonuca hiç girmez).
///   2) Her satır için: BuildRowActions(buttons, recordId) — adım 1 sonucunu o kaydın GERÇEK
///      id'siyle satır aksiyon nesnelerine çevirir (senkron, DB'ye gitmez, ucuz).
///
/// Yetki mantığı üç çağıranda (bu helper, IntegrationsController.ByFormApi,
/// IntegrationController.Run) TEK paylaşılan kurala bağlıdır: <see cref="CanRunManualButtonAsync"/>.
/// Kural (2026-07-26 sıkılaştırma): SystemAdmin her zaman geçer; PermissionDef VAR+aktif ise
/// PermissionService.CheckAsync grant kontrolü (DepartmentManager bypass'ı CheckAsync içinde
/// merkezî — SetupDefinitions hariç izinli); PermissionDef YOK/pasif ise YALNIZ SystemAdmin +
/// DepartmentManager (eski "tanım yoksa herkese serbest" geriye-uyumluluğu KALDIRILDI — fail-safe:
/// tanımsız buton dünyaya açık kalmaz, yönetici-only olur).
///
/// Üretilen aksiyon nesnesi TABLO görünümü (SmartTableRow.jsx) sözleşmesini hedefler:
/// {label, icon, color, apiUrl, apiMethod}. SmartTableRow.dispatchMenuAction yalnızca
/// action.apiUrl alanının VARLIĞINA bakar (secondaryAction/"Sil" ile aynı sözleşme — ayrıca
/// bkz. PurchaseController.cs "Onaya Gönder" örneği: {type:"api-post", url:"...{id}..."}
/// yalnızca SmartCard.jsx'in tanıdığı FARKLI bir sözleşmedir, SmartTableRow bunu anlamaz).
/// Bu yüzden apiUrl'e recordId GERÇEK değeriyle önceden gömülür ({id} placeholder'ı KULLANILMAZ).
/// SmartBoard varsayılan görünümü "table"dır (viewMode:'card' açıkça verilmedikçe) — bu
/// helper'ı viewMode:'card' olan bir board'da kullanmadan önce SmartCard tarafı ayrıca
/// doğrulanmalı (bkz. kullanım notu ByFormApi/Run referanslarında).
///
/// Referans: Helpers/AuditLogActionHelper.cs (extraActions[] deseni),
/// Controllers/IntegrationsController.cs ByFormApi (permission filtreleme deseni — bu
/// helper'ın adım 1'i onun birebir mirror'ıdır),
/// Controllers/IntegrationController.cs Run (tetikleme endpoint'i + yanıt şekli — {success,
/// error} zaten SmartTableRow.runMenuApiAction'ın beklediği {ok/success, error} sözleşmesiyle
/// uyumlu, ek sarmalama gerekmedi).
/// </summary>
public static class IntegrationButtonActionHelper
{
    /// <summary>
    /// Verilen FormCode için aktif + Manual tetikleyicili entegrasyon butonlarını getirir,
    /// kullanıcının çalıştırma yetkisi olmayanları eler (ortak kural: <see cref="CanRunManualButtonAsync"/>).
    /// Board request başına BİR KEZ çağrılır (per-row değil).
    /// </summary>
    public static async Task<IReadOnlyCollection<IntegrationManualButtonInfo>> GetAuthorizedManualButtonsAsync(
        string formCode,
        ClaimsPrincipal user,
        IIntegrationRepository integrationRepo,
        IPermissionDefRepository permDefRepo,
        IPermissionService permService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode))
            return Array.Empty<IntegrationManualButtonInfo>();

        var buttons = await integrationRepo.ListManualButtonsAsync(formCode.Trim(), ct);
        if (buttons.Count == 0)
            return buttons;

        var roleStr = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        UserAuthorizationCatalog.TryParseRole(roleStr, out var role);
        if (role == UserRole.SystemAdmin)
            return buttons; // kısa yol: per-buton DB sorgusu gerekmez

        var userId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? (int?)uid : null;
        int? deptId = int.TryParse(user.FindFirstValue("department_id"), out var d) && d > 0 ? d : null;

        var authorized = new List<IntegrationManualButtonInfo>();
        foreach (var btn in buttons)
        {
            if (await CanRunManualButtonAsync(role, userId, deptId, btn.SourceFormCode, btn.Id, permDefRepo, permService, ct))
                authorized.Add(btn);
        }
        return authorized;
    }

    /// <summary>
    /// Bir manuel entegrasyon butonunun (BUTTON:INT_{id}) belirli kullanıcı için çalıştırılabilir
    /// olup olmadığına dair TEK yetki kuralı — üç çağıran (bu helper'ın
    /// <see cref="GetAuthorizedManualButtonsAsync"/>'i, IntegrationsController.ByFormApi,
    /// IntegrationController.Run) buna bağlanır.
    ///
    /// Kural (2026-07-26 sıkılaştırma):
    ///   • SystemAdmin → her zaman izinli (DB sorgusu yok).
    ///   • PermissionDef VAR + aktif → IPermissionService.CheckAsync grant kontrolü
    ///     (DepartmentManager bypass'ı CheckAsync içinde merkezî — SetupDefinitions hariç izinli).
    ///   • PermissionDef YOK/pasif → YALNIZ SystemAdmin + DepartmentManager (fail-safe;
    ///     eski "tanım yoksa herkese serbest" geriye-uyumluluğu kaldırıldı).
    /// </summary>
    public static async Task<bool> CanRunManualButtonAsync(
        UserRole role, int? userId, int? departmentId,
        string sourceFormCode, int integrationId,
        IPermissionDefRepository permDefRepo,
        IPermissionService permService,
        CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return true;

        var actionCode = PermissionDefDiscoveryService.BuildIntegrationButtonActionCode(integrationId);
        var def = await permDefRepo.GetByFormAndActionAsync(sourceFormCode, actionCode, ct);

        if (def is { IsActive: true })
        {
            // Tanım var → normal grant kontrolü (DepartmentManager bypass CheckAsync içinde).
            if (userId is null || userId <= 0) return false;
            return await permService.CheckAsync(userId.Value, role, departmentId, sourceFormCode, actionCode, ct);
        }

        // Tanım yok/pasif → yalnız yönetici rolleri (SystemAdmin yukarıda döndü).
        return role == UserRole.DepartmentManager;
    }

    /// <summary>
    /// GetAuthorizedManualButtonsAsync'ten dönen (yetki-filtrelenmiş) listeyi, belirli bir kayıt
    /// için SmartBoard satır menüsü aksiyon nesnelerine çevirir. apiUrl'e GERÇEK recordId gömülür.
    /// </summary>
    public static object[] BuildRowActions(IReadOnlyCollection<IntegrationManualButtonInfo> buttons, int recordId)
    {
        if (buttons.Count == 0) return Array.Empty<object>();
        return buttons.Select(btn => (object)new
        {
            id        = $"int_{btn.Id}",
            label     = string.IsNullOrWhiteSpace(btn.ButtonLabel) ? btn.Name : btn.ButtonLabel,
            icon      = "Send",
            color     = "violet",
            apiUrl    = $"/Integration/Run/{btn.Id}?recordId={recordId}",
            apiMethod = "POST",
        }).ToArray();
    }
}
