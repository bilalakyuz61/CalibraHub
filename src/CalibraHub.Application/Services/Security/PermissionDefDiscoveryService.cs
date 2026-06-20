using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Security;

/// <summary>
/// 2026-06-06 — Startup discovery: dbo.Forms tablosundaki her form için 6 standart action'ı
/// PermissionDef tablosuna upsert eder. Yeni form eklenirse bir sonraki başlangıçta otomatik
/// catalog'a girer.
///
/// **Idempotent:** Mevcut (FormCode, ActionCode) varsa Label/SortOrder güncellenir.
///
/// **Form-içi özel butonlar:** Bu service yalnızca CRUD action'ları seed eder; BUTTON:*
/// action'ları admin'in form designer'da elle eklemesi beklenir (F3 ile UI'dan).
/// </summary>
public sealed class PermissionDefDiscoveryService
{
    private readonly IFormRepository _formRepo;
    private readonly IPermissionDefRepository _permRepo;
    private readonly IWidgetRepository _widgetRepo;
    private readonly IPermissionService _permService;
    private readonly IReportDesignRepository _designRepo;

    public PermissionDefDiscoveryService(
        IFormRepository formRepo,
        IPermissionDefRepository permRepo,
        IWidgetRepository widgetRepo,
        IPermissionService permService,
        IReportDesignRepository designRepo)
    {
        _formRepo    = formRepo;
        _permRepo    = permRepo;
        _widgetRepo  = widgetRepo;
        _permService = permService;
        _designRepo  = designRepo;
    }

    /// <summary>Field permission action prefix — <c>FIELD:&lt;WidgetCode&gt;</c>.</summary>
    public const string FieldActionPrefix = "FIELD:";

    /// <summary>Dashboard (rapor panosu) resource permission action prefix — <c>RESOURCE:DASH:&lt;id&gt;</c>.
    /// Her dashboard'a per-resource yetki. DASHBOARDS form'unda VIEW = ekrana giriş; bu prefix =
    /// belirli bir panoyu görme.</summary>
    public const string DashboardActionPrefix = "RESOURCE:DASH:";
    public static string BuildDashboardActionCode(int dashboardId) => DashboardActionPrefix + dashboardId;

    /// <summary>
    /// Form bazlı standart action devre dışı listesi — buradaki (FormCode → actionCodes) için
    /// startup'ta IsActive=false ile seed edilir, Yetki Yönetimi UI'da görünmez.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> FormInactiveStandardActions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // WhatsApp: kişisel/departman kapsamlı görüntüleme/düzenleme/silme anlamsız.
            // Aktif: VIEW (İzleme Genel), CREATE (Mesaj Gönderme), DELETE_OWN (Mesaj Silme).
            [FormCodes.WhatsApp] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionDef.StandardActions.ViewOwn,
                PermissionDef.StandardActions.ViewDept,
                PermissionDef.StandardActions.EditOwn,
                PermissionDef.StandardActions.EditDept,
                PermissionDef.StandardActions.EditAll,
                PermissionDef.StandardActions.DeleteDept,
                PermissionDef.StandardActions.DeleteAll,
            },
            // ApprovalPending: salt okunur liste — sadece VIEW üçlüsü (Özel/Departman/Genel) anlamlı.
            [FormCodes.ApprovalPending] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionDef.StandardActions.Create,
                PermissionDef.StandardActions.EditOwn,
                PermissionDef.StandardActions.EditDept,
                PermissionDef.StandardActions.EditAll,
                PermissionDef.StandardActions.DeleteOwn,
                PermissionDef.StandardActions.DeleteDept,
                PermissionDef.StandardActions.DeleteAll,
            },
            // DASHBOARDS: ekran erişimi rol-tabanlı; bireysel rapor erişimi RESOURCE:DASH:{id}
            // özel aksiyon ile kontrol edilir — standart CRUD aksiyonları tümü görünmez.
            [FormCodes.Dashboards] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionDef.StandardActions.View,
                PermissionDef.StandardActions.ViewOwn,
                PermissionDef.StandardActions.ViewDept,
                PermissionDef.StandardActions.Create,
                PermissionDef.StandardActions.EditOwn,
                PermissionDef.StandardActions.EditDept,
                PermissionDef.StandardActions.EditAll,
                PermissionDef.StandardActions.DeleteOwn,
                PermissionDef.StandardActions.DeleteDept,
                PermissionDef.StandardActions.DeleteAll,
            },
        };

    /// <summary>
    /// Form bazlı standart action label override'ları — bağlam spesifik etiketler.
    /// Örn. WhatsApp'ta CREATE = "Yeni Kayıt" yerine "Mesaj Gönderme".
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> FormActionLabelOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [FormCodes.WhatsApp] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [PermissionDef.StandardActions.Create]    = "Mesaj Gönderme",
                [PermissionDef.StandardActions.DeleteOwn] = "Mesaj Silme",
            },
            // Rapor Panoları: VIEW = ekrana giriş ("Rapor Panoları" sayfasını açabilme).
            // Tek tek rapor erişimi RESOURCE:DASH:{id} kayıtlarıyla yönetilir.
            [FormCodes.Dashboards] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [PermissionDef.StandardActions.View] = "Ekrana Erişim",
            },
        };

    /// <summary>
    /// Master-detail formlarda kalem/liste alt form'ları parent'tan miras alır — ayrı
    /// PermissionDef oluşturmaz. UI kirliliği ve mantıksal karışıklığı engeller.
    /// </summary>
    private static readonly HashSet<string> ExcludedFormNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Master-detail alt formları (parent'tan miras alır)
            // NOT: "Liste" burada kasıtlı yok — üst-seviye liste formları (SALES_QUOTE, MATERIAL_CARD_EDIT vb.)
            // artık tanımlayıcı FormName taşıyor ("Satış Teklifi", "Malzeme Kartları" vb.).
            "Kalem Bilgisi",
            "Detay",
            "Alt Kalem",
            "Satır",
            // Master-detail başlık/oluşturma alt formları
            "Üst Bilgi",
            "Yeni",
            "Header",
            "New",
        };

    /// <summary>
    /// Her FormCode için 6 standart action'ı (VIEW, CREATE, EDIT_OWN, EDIT_ALL,
    /// DELETE_OWN, DELETE_ALL) PermissionDef tablosuna ekler/günceller.
    /// Master-detail kalem/liste alt formları seed dışı bırakılır.
    /// </summary>
    public async Task<int> DiscoverAndSeedAsync(CancellationToken ct)
    {
        var forms = await _formRepo.GetAllAsync(ct);
        if (forms.Count == 0)
        {
            Console.WriteLine("[PERM DISCOVERY] dbo.Forms boş — seed atlandı.");
            return 0;
        }

        var defs = new List<PermissionDef>(forms.Count * PermissionDef.StandardActions.All.Count);
        var skippedCount = 0;

        foreach (var form in forms)
        {
            if (!form.IsActive) continue;

            // Master-detail alt formları parent'tan miras alır — yetki katalog'una eklenmez.
            if (form.FormName != null && ExcludedFormNames.Contains(form.FormName.Trim()))
            {
                skippedCount++;
                continue;
            }

            // Form için standart action'ları seed et. Sıralama: VIEW_OWN=0, VIEW=10, CREATE=20, ...
            // FormInactiveStandardActions'da yer alanlar IsActive=false ile seed edilir (UI'da görünmez).
            // FormActionLabelOverrides ile form-spesifik etiketler uygulanır.
            FormInactiveStandardActions.TryGetValue(form.FormCode, out var inactiveSet);
            FormActionLabelOverrides.TryGetValue(form.FormCode, out var labelOverrides);
            var sortOrder = 0;
            foreach (var actionCode in PermissionDef.StandardActions.All)
            {
                var isActive   = inactiveSet == null || !inactiveSet.Contains(actionCode);
                var actionLabel = labelOverrides != null && labelOverrides.TryGetValue(actionCode, out var custom)
                    ? custom
                    : GetActionLabel(actionCode);
                defs.Add(new PermissionDef
                {
                    FormCode   = form.FormCode,
                    ActionCode = actionCode,
                    Label      = $"{form.FormName} — {actionLabel}",
                    Category   = PermissionDef.Categories.Crud,
                    SortOrder  = sortOrder,
                    IsActive   = isActive,
                    // System discovery — no user context; CreatedById = null.
                });
                sortOrder += 10;
            }

            // ── BUTTON:* — Form içi özel buton izinleri (FormButtonCatalog'tan) ─────
            // Yetki Yönetimi'nde CRUD action'larının ALTINDA listelensin diye SortOrder >= 100.
            if (FormButtonCatalog.Buttons.TryGetValue(form.FormCode, out var buttons))
            {
                var btnSortOrder = 100;
                foreach (var btn in buttons)
                {
                    defs.Add(new PermissionDef
                    {
                        FormCode   = form.FormCode,
                        ActionCode = FormButtonCatalog.BuildActionCode(btn.Key),
                        Label      = $"{form.FormName} — {btn.Label}",
                        Category   = PermissionDef.Categories.Action,
                        SortOrder  = btnSortOrder,
                        IsActive   = true,
                        // System discovery — no user context; CreatedById = null.
                    });
                    btnSortOrder += 10;
                }
            }
        }

        // ── FIELD:* — Yetkilendirilebilir alan izinleri (WidgetMas.IsPermissionControlled=1) ──
        // Form bağımsız akış: WidgetMas'taki tüm aktif yetkilendirilebilir widget'lar tek tek seed.
        // FormCode → FormId çözümü için forms dict'i kullan.
        var fieldDefCount = 0;
        try
        {
            var allForms = await _widgetRepo.GetFormsAsync(ct);
            foreach (var formDef in allForms)
            {
                var widgets = await _widgetRepo.GetWidgetsByFormAsync(formDef.Id, ct, includeInactive: false);
                var fieldSortOrder = 500;
                foreach (var w in widgets)
                {
                    if (!w.IsPermissionControlled) continue;
                    if (string.IsNullOrWhiteSpace(w.WidgetCode)) continue;

                    defs.Add(new PermissionDef
                    {
                        FormCode   = formDef.FormCode,
                        ActionCode = $"{FieldActionPrefix}{w.WidgetCode.Trim().ToUpperInvariant()}",
                        Label      = $"{formDef.FormName ?? formDef.FormCode} — Alan: {w.Label}",
                        Category   = PermissionDef.Categories.Action,
                        SortOrder  = fieldSortOrder,
                        IsActive   = true,
                        // System discovery — no user context; CreatedById = null.
                    });
                    fieldSortOrder += 10;
                    fieldDefCount++;
                }
            }
        }
        catch (Exception ex)
        {
            // WidgetMas tablosu henüz oluşmamış olabilir (ilk init); discovery zincirini bozma.
            Console.WriteLine($"[PERM DISCOVERY] Field permission seed atlandı: {ex.Message}");
        }

        // ── RESOURCE:DASH:{id} — Her rapor panosu için ayrı PermissionDef ──
        // DASHBOARDS form'u sadece VIEW (ekrana giriş) taşır; tek tek rapor erişimi
        // bu kayıtlarla yönetilir. SortOrder >= 200, kayıt sırasına göre.
        var dashDefCount = 0;
        try
        {
            var dashboards = await _designRepo.GetAllAsync(ct);
            var dashSortOrder = 200;
            foreach (var d in dashboards)
            {
                defs.Add(new PermissionDef
                {
                    FormCode   = FormCodes.Dashboards,
                    ActionCode = BuildDashboardActionCode(d.Id),
                    Label      = $"Rapor Panoları — {d.Title}",
                    Category   = PermissionDef.Categories.Report,
                    SortOrder  = dashSortOrder,
                    IsActive   = true,
                });
                dashSortOrder += 10;
                dashDefCount++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PERM DISCOVERY] Dashboard resource permission seed atlandı: {ex.Message}");
        }

        await _permRepo.BulkUpsertAsync(defs, ct);
        var btnDefCount = defs.Count(d => d.ActionCode.StartsWith(FormButtonCatalog.ActionPrefix, StringComparison.Ordinal));
        var crudDefCount = defs.Count - btnDefCount - fieldDefCount - dashDefCount;
        Console.WriteLine($"[PERM DISCOVERY] {defs.Count} izin tanımı upsert edildi (CRUD: {crudDefCount}, Buton: {btnDefCount}, Alan: {fieldDefCount}, Pano: {dashDefCount}; {skippedCount} master-detail alt form atlandı).");
        return defs.Count;
    }

    /// <summary>
    /// Dashboard kaydedildiğinde / silindiğinde anında çağrılır — yeniden başlatma beklenmez.
    /// <para>
    /// <c>isActive=true</c> (kayıt/güncelleme) → <c>RESOURCE:DASH:&lt;id&gt;</c> PermissionDef upsert
    /// ("Pano: {title}").<br/>
    /// <c>isActive=false</c> (silme) → PermissionDef IsActive=false (orphan UserPermission/
    /// DepartmentPermission kayıtları korunur ki yanlışlıkla silinen rapor geri eklenirse
    /// yetkiler hemen geçerli olur).
    /// </para>
    /// </summary>
    public async Task SyncDashboardPermissionAsync(int dashboardId, string title, bool isActive, CancellationToken ct)
    {
        if (dashboardId <= 0) return;
        var def = new PermissionDef
        {
            FormCode   = FormCodes.Dashboards,
            ActionCode = BuildDashboardActionCode(dashboardId),
            Label      = $"Rapor Panoları — {(string.IsNullOrWhiteSpace(title) ? "#" + dashboardId : title)}",
            Category   = PermissionDef.Categories.Report,
            SortOrder  = 200 + dashboardId,
            IsActive   = isActive,
        };
        await _permRepo.BulkUpsertAsync([def], ct);
        _permService.InvalidateDefsCache();
    }

    /// <summary>
    /// Widget kaydedilince anında çağrılır — yeniden başlatma beklenmez.
    /// <para>
    /// <c>isPermissionControlled=true</c> → <c>FIELD:&lt;WidgetCode&gt;</c> PermissionDef upsert.<br/>
    /// <c>isPermissionControlled=false</c> → mevcutsa sil.
    /// </para>
    /// </summary>
    public async Task SyncFieldPermissionAsync(
        string formCode,
        string formName,
        string widgetCode,
        string widgetLabel,
        bool isPermissionControlled,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode) || string.IsNullOrWhiteSpace(widgetCode))
            return;

        var actionCode = $"{FieldActionPrefix}{widgetCode.Trim().ToUpperInvariant()}";

        // Label formatı startup discovery ile aynı: "FormName — Alan: WidgetLabel"
        // Yetki Yönetimi UI label'ı " — " split ile parse eder; prefix yoksa actionCode'a düşer.
        var displayFormName = string.IsNullOrWhiteSpace(formName) ? formCode : formName;

        // IsActive = isPermissionControlled — silmek yerine aktif/pasif toggle.
        // Böylece mevcut rol atamaları (PermissionGrant) bozulmaz; toggle geri açılınca
        // atamalar hemen geçerli olur. BulkUpsertAsync MERGE kullanır — UQ ihlali yok.
        var def = new PermissionDef
        {
            FormCode   = formCode,
            ActionCode = actionCode,
            Label      = $"{displayFormName} — Alan: {widgetLabel}",
            Category   = PermissionDef.Categories.Action,
            SortOrder  = 500,
            IsActive   = isPermissionControlled,
            // System widget-save — no user context; CreatedById = null.
        };
        await _permRepo.BulkUpsertAsync([def], ct);

        // Defs cache'ini anında temizle — 60sn TTL'yi bekleme.
        _permService.InvalidateDefsCache();
    }

    private static string GetActionLabel(string actionCode) => actionCode switch
    {
        PermissionDef.StandardActions.ViewOwn    => "İzleme (Özel)",
        PermissionDef.StandardActions.ViewDept   => "İzleme (Departman)",
        PermissionDef.StandardActions.View       => "İzleme (Genel)",
        PermissionDef.StandardActions.Create     => "Yeni Kayıt",
        PermissionDef.StandardActions.EditOwn    => "Düzenleme (Özel)",
        PermissionDef.StandardActions.EditDept   => "Düzenleme (Departman)",
        PermissionDef.StandardActions.EditAll    => "Düzenleme (Genel)",
        PermissionDef.StandardActions.DeleteOwn  => "Silme (Özel)",
        PermissionDef.StandardActions.DeleteDept => "Silme (Departman)",
        PermissionDef.StandardActions.DeleteAll  => "Silme (Genel)",
        _ => actionCode,
    };
}
