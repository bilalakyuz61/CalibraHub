using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval;
using CalibraHub.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Approval;

/// <summary>
/// Approval SLA bildirim gondericisi — Worker tarafindan SLA timer'i
/// her tetiklendiginde "pre-warning" veya "overdue reminder" gondermek
/// icin cagrilir. MVP: email kanali. WhatsApp opsiyonel (TODO).
/// </summary>
public interface IApprovalNotificationDispatcher
{
    /// <param name="kind">"warn" (DueDate yaklasiyor) | "overdue" (asildi)</param>
    Task SendReminderAsync(OverdueStepRecord rec, string kind, CancellationToken ct);

    /// <summary>
    /// Faz 4 — Designer "Notification" node'undan tetiklenen serbest gönderim.
    /// nodeData JSON: notificationType (mail|whatsapp|both), recipientMode
    /// (creator|approver|specificUser|department|custom), recipientId, subject,
    /// body, attachPdf. Token: {documentNumber}, {documentId}, {approverName} vb.
    /// Entity-agnostic — header dictionary'sinden token değerleri çekilir.
    /// </summary>
    Task SendFromNodeAsync(string? nodeDataJson, ApprovalEntityContext ctx, CancellationToken ct);
}

public sealed class ApprovalNotificationDispatcher : IApprovalNotificationDispatcher
{
    private readonly IEmailSender _email;
    private readonly IUserProfileRepository _userRepo;
    private readonly IApprovalInstanceRepository _instRepo;
    private readonly ILogger<ApprovalNotificationDispatcher> _logger;
    private readonly IWhatsAppService? _whatsApp;
    private readonly IDocDesignerService? _designer;
    private readonly IDocLayoutRuleService? _layoutRules;
    private readonly IApprovalTokenRepository? _tokenRepo;
    private readonly ICurrentCompanyProvider? _companyProvider;
    private readonly ICompanyRepository? _companyRepo;

    public ApprovalNotificationDispatcher(
        IEmailSender email,
        IUserProfileRepository userRepo,
        IApprovalInstanceRepository instRepo,
        ILogger<ApprovalNotificationDispatcher> logger,
        IWhatsAppService? whatsApp = null,
        IDocDesignerService? designer = null,
        IDocLayoutRuleService? layoutRules = null,
        IApprovalTokenRepository? tokenRepo = null,
        ICurrentCompanyProvider? companyProvider = null,
        ICompanyRepository? companyRepo = null)
    {
        _email           = email;
        _userRepo        = userRepo;
        _instRepo        = instRepo;
        _logger          = logger;
        _whatsApp        = whatsApp;
        _designer        = designer;
        _layoutRules     = layoutRules;
        _tokenRepo       = tokenRepo;
        _companyProvider = companyProvider;
        _companyRepo     = companyRepo;
    }

    public async Task SendReminderAsync(OverdueStepRecord rec, string kind, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rec.ApproverId))
        {
            _logger.LogDebug("SLA reminder skipped — ApproverId bos (record={Id}).", rec.StepRecordId);
            return;
        }

        // ApproverId artık int string'i. UserProfileRepository.GetByIdAsync int kabul ediyor.
        if (!int.TryParse(rec.ApproverId, out var userIdInt))
        {
            _logger.LogDebug("SLA reminder skipped — ApproverId int degil ({Aid}, record={Id}).",
                rec.ApproverId, rec.StepRecordId);
            return;
        }

        var user = await _userRepo.GetByIdAsync(userIdInt, ct);
        if (user is null)
        {
            _logger.LogWarning("SLA reminder skipped — user bulunamadi (Aid={Aid}, record={Id}).",
                rec.ApproverId, rec.StepRecordId);
            return;
        }

        var docLabel = !string.IsNullOrWhiteSpace(rec.DocumentNumber)
            ? rec.DocumentNumber!
            : rec.DocumentId?.ToString() ?? "—";

        var subject = kind == "warn"
            ? $"[Hatirlatma] Onay suresi yaklasiyor — {docLabel}"
            : $"[Uyari] Onay suresi asildi — {docLabel}";

        var body = ApplyTokens(
            rec.SlaMessageTemplate
            ?? "Merhaba {approverName},\n\n{flowName} icin {documentNumber} numarali belgenin {stepName} adimi {dueDate} tarihine kadar tamamlanmali.\n\nCalibraHub uzerinden adimi acabilirsiniz.",
            rec, user.FullName);

        try
        {
            var result = await _email.SendAsync(
                user.CompanyId,
                new[] { user.Email },
                subject,
                body,
                attachments: null,
                ct);
            if (result.Status == EmailStatus.Sent)
                _logger.LogInformation("SLA {Kind} email gonderildi (record={Id}, email={Email}).",
                    kind, rec.StepRecordId, user.Email);
            else
                _logger.LogWarning("SLA {Kind} email {Status}: {Msg} (record={Id}).",
                    kind, result.Status, result.Message ?? "(bos)", rec.StepRecordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SLA reminder gonderim hatasi (record={Id}).", rec.StepRecordId);
        }

        // TODO: WhatsApp kanali — Step nodeData icinde notifyChannel destegi eklendiginde.
    }

    private static string ApplyTokens(string template, OverdueStepRecord rec, string approverName)
    {
        var dueLocal = rec.DueDate?.ToLocalTime();
        return template
            .Replace("{approverName}", approverName ?? "")
            .Replace("{documentNumber}", string.IsNullOrWhiteSpace(rec.DocumentNumber) ? rec.DocumentId?.ToString() ?? "—" : rec.DocumentNumber!)
            .Replace("{documentId}", rec.DocumentId?.ToString() ?? "—")
            .Replace("{dueDate}", dueLocal?.ToString("dd.MM.yyyy HH:mm") ?? "—")
            .Replace("{stepName}", rec.StepName ?? "")
            .Replace("{flowName}", rec.FlowName ?? "");
    }

    // ── Faz 4: Notification node dispatch ────────────────────────────────────
    public async Task SendFromNodeAsync(string? nodeDataJson, ApprovalEntityContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeDataJson))
        {
            _logger.LogDebug("Notification node dispatch — nodeData boş, atlandı.");
            return;
        }

        NotificationNodeConfig cfg;
        try
        {
            cfg = ParseConfig(nodeDataJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification nodeData parse hatası — gönderim atlandı.");
            return;
        }

        // Alıcıları çöz
        var recipients = await ResolveRecipientsAsync(cfg, ctx, ct);
        if (recipients.Count == 0)
        {
            _logger.LogDebug("Notification dispatch — alıcı bulunamadı (mode={Mode}, id={Id}).", cfg.RecipientMode, cfg.RecipientId);
            return;
        }

        // Hızlı onay/red link tokenları — {approveLink} / {rejectLink} şablon içinde varsa
        // her alıcı için ayrı token üret; link yoksa boş string (şablondan kaldırılır).
        var rawSubject = string.IsNullOrWhiteSpace(cfg.Subject) ? "Onay Bildirimi" : cfg.Subject;
        var rawBody    = cfg.Body ?? "";
        bool hasLinkToken = rawSubject.Contains("{approveLink}") || rawSubject.Contains("{rejectLink}")
                         || rawBody.Contains("{approveLink}")    || rawBody.Contains("{rejectLink}");
        var needLinks  = _tokenRepo is not null
            && ctx.ApprovalInstanceId.HasValue
            && hasLinkToken;

        _logger.LogWarning("[LINK-DBG] SendFromNodeAsync — tokenRepo={Repo} instanceId={Inst} hasLinkToken={Tok} needLinks={Need} ctxBaseUrl={CtxUrl} recipients={Rcp}",
            _tokenRepo != null, ctx.ApprovalInstanceId, hasLinkToken, needLinks,
            ctx.BaseUrl ?? "(null)",
            string.Join(", ", recipients.Select(r => $"{r.Name}[cid={r.CompanyId}]")));

        // Şirkete ait dış erişim URL'i (PublicBaseUrl öncelikli, yoksa request URL).
        var baseUrl = (string?)null;
        if (needLinks)
        {
            var companyId = recipients.FirstOrDefault(r => r.CompanyId > 0)?.CompanyId ?? 0;
            if (companyId > 0 && _companyRepo is not null)
            {
                var co = await _companyRepo.GetByIdAsync(companyId, ct);
                baseUrl = co?.PublicBaseUrl;
            }
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = _companyProvider?.GetBaseUrl();
            // HTTP context yok (Task.Run fire-and-forget): ApprovalFlowService context'i açıkken yakaladığı URL'i kullan.
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = ctx.BaseUrl;

            _logger.LogInformation(
                "QuickLink check — tokenRepo={HasRepo} instanceId={InstId} hasToken={HasTok} baseUrl={Url} companyId={Co}",
                _tokenRepo is not null, ctx.ApprovalInstanceId, hasLinkToken, baseUrl ?? "(null)",
                recipients.FirstOrDefault(r => r.CompanyId > 0)?.CompanyId ?? 0);
        }

        // Per-recipient token üretimi + dispatch
        if (needLinks && !string.IsNullOrWhiteSpace(baseUrl))
        {
            var lnkCompanyId = recipients.FirstOrDefault(r => r.CompanyId > 0)?.CompanyId ?? 0;
            var lnkType      = (cfg.NotificationType ?? "mail").Trim().ToLowerInvariant();
            var lnkSendMail  = lnkType is "mail" or "both" or "email";
            var lnkSendWa    = lnkType is "whatsapp" or "both";

            List<EmailAttachment>? lnkAttachments = null;
            if (cfg.AttachPdf)
            {
                var pdf = await TryBuildPdfAttachmentAsync(ctx, ct);
                if (pdf is not null) lnkAttachments = new List<EmailAttachment> { pdf };
            }

            foreach (var rec in recipients)
            {
                var approverId = rec.UserId ?? "";
                string approveLink = "", rejectLink = "";
                if (!string.IsNullOrWhiteSpace(approverId) && ctx.ApprovalInstanceId.HasValue)
                {
                    try
                    {
                        var approveToken = await _tokenRepo!.CreateAsync(ctx.ApprovalInstanceId.Value, null, approverId, ct);
                        var rejectToken  = await _tokenRepo!.CreateAsync(ctx.ApprovalInstanceId.Value, null, approverId, ct);
                        approveLink = $"{baseUrl}/Approval/Quick?c={lnkCompanyId}&t={approveToken}&a=approve";
                        rejectLink  = $"{baseUrl}/Approval/Quick?c={lnkCompanyId}&t={rejectToken}&a=reject";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Hızlı onay token üretilemedi (instanceId={Id}, approverId={Ap}).",
                            ctx.ApprovalInstanceId, approverId);
                    }
                }

                var subj    = ApplyContextTokens(rawSubject, ctx, rec.Name, approveLink, rejectLink);
                var lnkBody = ApplyContextTokens(rawBody,    ctx, rec.Name, approveLink, rejectLink);

                if (lnkSendMail && !string.IsNullOrWhiteSpace(rec.Email))
                {
                    try
                    {
                        var result = await _email.SendAsync(rec.CompanyId, new[] { rec.Email! }, subj, lnkBody, lnkAttachments, ct);
                        _logger.LogInformation("Notification (link) email: {Status} → {Email}.", result.Status, rec.Email);
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Notification (link) email hatası ({Email}).", rec.Email); }
                }

                if (lnkSendWa && _whatsApp is not null && !string.IsNullOrWhiteSpace(rec.Phone))
                {
                    try
                    {
                        var msg = string.IsNullOrWhiteSpace(lnkBody) ? subj
                            : (string.IsNullOrWhiteSpace(subj) ? lnkBody : subj + "\n\n" + lnkBody);
                        var r = await _whatsApp.SendTextMessageAsync(rec.Phone!, msg, ct, interactive: true);
                        if (!r.Success) _logger.LogWarning("Notification (link) WhatsApp başarısız: {Msg}", r.Message);
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Notification (link) WhatsApp hatası ({Phone}).", rec.Phone); }
                }
            }
            return;
        }

        // ── Standart yol (link token yok) ─────────────────────────────────────
        // Token replace — boş string null değil, ?? çalışmaz; IsNullOrWhiteSpace kullan.
        var subject = ApplyContextTokens(rawSubject, ctx, recipients[0].Name);
        var body    = ApplyContextTokens(rawBody, ctx, recipients[0].Name);

        // PDF ek (opsiyonel)
        List<EmailAttachment>? attachments = null;
        if (cfg.AttachPdf)
        {
            var pdf = await TryBuildPdfAttachmentAsync(ctx, ct);
            if (pdf is not null) attachments = new List<EmailAttachment> { pdf };
        }

        // Kanallara göre dispatch
        var type = (cfg.NotificationType ?? "mail").Trim().ToLowerInvariant();
        var sendMail     = type is "mail" or "both" or "email";
        var sendWhatsApp = type is "whatsapp" or "both";

        if (sendMail)
        {
            var emails = recipients.Where(r => !string.IsNullOrWhiteSpace(r.Email)).Select(r => r.Email!).Distinct().ToList();
            if (emails.Count > 0)
            {
                try
                {
                    var companyId = recipients.First(r => !string.IsNullOrWhiteSpace(r.Email)).CompanyId;
                    var result = await _email.SendAsync(companyId, emails, subject, body, attachments, ct);
                    _logger.LogInformation("Notification node email: {Status} → {Count} alıcı.",
                        result.Status, emails.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Notification node email gönderim hatası.");
                }
            }
        }

        if (sendWhatsApp && _whatsApp is not null)
        {
            foreach (var rec in recipients.Where(r => !string.IsNullOrWhiteSpace(r.Phone)))
            {
                try
                {
                    // subject artık asla boş değil (yukarıda default "Onay Bildirimi" atandı).
                    var msg = string.IsNullOrWhiteSpace(body)
                        ? subject
                        : (string.IsNullOrWhiteSpace(subject) ? body : subject + "\n\n" + body);
                    // interactive: true → rate limit + insan gecikmesi atlanır; UI ile aynı hız.
                    // Onay bildirimi spam değil — sistem tarafından tek mesaj, normal UI gönderimi gibi.
                    var r = await _whatsApp.SendTextMessageAsync(rec.Phone!, msg, ct, interactive: true);
                    if (!r.Success)
                        _logger.LogWarning("Notification WhatsApp gönderim başarısız: {Msg}", r.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Notification WhatsApp gönderim hatası ({Phone}).", rec.Phone);
                }
            }
        }
    }

    private static NotificationNodeConfig ParseConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new NotificationNodeConfig
        {
            NotificationType = TryStr(root, "notificationType"),
            RecipientMode    = TryStr(root, "recipientMode") ?? "creator",
            RecipientId      = TryStr(root, "recipientId"),
            CustomEmail      = TryStr(root, "customEmail"),
            CustomPhone      = TryStr(root, "customPhone"),
            Subject          = TryStr(root, "subject"),
            Body             = TryStr(root, "body"),
            AttachPdf        = TryBool(root, "attachPdf"),
        };
    }

    private static string? TryStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    private static bool TryBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private async Task<List<NotifyRecipient>> ResolveRecipientsAsync(
        NotificationNodeConfig cfg, ApprovalEntityContext ctx, CancellationToken ct)
    {
        var list = new List<NotifyRecipient>();
        var mode = (cfg.RecipientMode ?? "creator").Trim();

        switch (mode.ToLowerInvariant())
        {
            case "creator":
                // ctx.HeaderValues["user.userId"] (Document entity tipinden gelir).
                // Diğer entity tipleri "creator" alanını henüz tanımlamıyorsa atlanır.
                var creatorId = TryGetInt(ctx, "user.userId");
                if (creatorId.HasValue)
                {
                    var u = await _userRepo.GetByIdAsync(creatorId.Value, ct);
                    if (u is not null) list.Add(NotifyRecipient.From(u));
                }
                break;

            case "managerofcreator":
                // Belgeyi oluşturan kullanıcının amiri (Users.SupervisorUserId).
                var morCreatorId = TryGetInt(ctx, "user.userId");
                if (morCreatorId.HasValue)
                {
                    var creator = await _userRepo.GetByIdAsync(morCreatorId.Value, ct);
                    if (creator?.SupervisorUserId.HasValue == true)
                    {
                        var mgr = await _userRepo.GetByIdAsync(creator.SupervisorUserId.Value, ct);
                        if (mgr is not null) list.Add(NotifyRecipient.From(mgr));
                    }
                }
                break;

            case "specificuser":
                if (int.TryParse(cfg.RecipientId, out var uid))
                {
                    var u = await _userRepo.GetByIdAsync(uid, ct);
                    if (u is not null) list.Add(NotifyRecipient.From(u));
                }
                break;

            case "department":
                if (int.TryParse(cfg.RecipientId, out var depId))
                {
                    var all = await _userRepo.GetAllAsync(ct);
                    foreach (var u in all.Where(x => x.DepartmentId == depId))
                        list.Add(NotifyRecipient.From(u));
                }
                break;

            case "approver":
                // ctx.ApprovalInstanceId, executor tarafından notification case'inde set edilir.
                // Instance'ın current step'ini bul ve o adımın ApproverId'sini bildirim hedefi yap.
                if (ctx.ApprovalInstanceId.HasValue)
                {
                    var inst = await _instRepo.GetByIdAsync(ctx.ApprovalInstanceId.Value, ct);
                    if (inst is not null)
                    {
                        // CurrentStep'e karşılık gelen step record'unun ApproverId'sini al.
                        // Step record henüz Pending olabilir; ApproverId flow tanımından gelir.
                        var sr = inst.StepRecords.FirstOrDefault(s => s.StepOrder == inst.CurrentStep);
                        var approverId = sr?.ApproverId;
                        if (!string.IsNullOrEmpty(approverId) && int.TryParse(approverId, out var apId))
                        {
                            var u = await _userRepo.GetByIdAsync(apId, ct);
                            if (u is not null) list.Add(NotifyRecipient.From(u));
                        }
                    }
                }
                break;

            case "custom":
                if (!string.IsNullOrWhiteSpace(cfg.CustomEmail) || !string.IsNullOrWhiteSpace(cfg.CustomPhone))
                {
                    list.Add(new NotifyRecipient
                    {
                        Name = cfg.CustomEmail ?? cfg.CustomPhone ?? "custom",
                        Email = cfg.CustomEmail,
                        Phone = cfg.CustomPhone,
                        CompanyId = 0,
                    });
                }
                break;
        }
        return list;
    }

    private static string ApplyContextTokens(
        string template,
        ApprovalEntityContext ctx,
        string recipientName,
        string approveLink = "",
        string rejectLink  = "")
    {
        // Header dict'inden field değerlerini token olarak çek; eksik alanlar boş.
        string Get(string code, string format = "")
        {
            if (!ctx.HeaderValues.TryGetValue(code, out var v) || v is null) return "";
            if (v is DateTime dt) return dt.ToString(format == "" ? "dd.MM.yyyy" : format);
            if (v is decimal dec) return dec.ToString(format == "" ? "N2" : format);
            return v.ToString() ?? "";
        }
        var docNumber = Get("documentNumber");
        if (string.IsNullOrEmpty(docNumber)) docNumber = ctx.EntityId ?? "";
        var result = (template ?? "")
            .Replace("{documentNumber}", docNumber)
            .Replace("{documentId}",     ctx.EntityId ?? "")
            .Replace("{entityId}",       ctx.EntityId ?? "")
            .Replace("{entityType}",     ctx.EntityTypeCode ?? "")
            .Replace("{documentDate}",   Get("documentDate"))
            .Replace("{documentKind}",   ctx.EntityTypeCode ?? "")
            .Replace("{amount}",         Get("amount"))
            .Replace("{contactName}",    Get("contactName"))
            .Replace("{taxNo}",          Get("taxNo"))
            .Replace("{approverName}",   recipientName ?? "")
            .Replace("{recipientName}",  recipientName ?? "")
            .Replace("{requesterName}",  ctx.RequesterName ?? "")
            .Replace("{flowName}",       ctx.FlowName ?? "")
            .Replace("{currentStepName}", "");

        // {var.degiskenAdi} — FlowVariables'dan resolve et.
        // Header token'larla çakışmayan, akışa özel dinamik değerler için.
        if (ctx.FlowVariables.Count > 0 && result.Contains("{var."))
        {
            foreach (var (key, val) in ctx.FlowVariables)
            {
                var token = "{var." + key + "}";
                if (!result.Contains(token)) continue;
                var strVal = val switch
                {
                    null           => "",
                    DateTime dt    => dt.ToString("dd.MM.yyyy"),
                    decimal dec    => dec.ToString("N2"),
                    double dbl     => ((decimal)dbl).ToString("N2"),
                    _              => val.ToString() ?? "",
                };
                result = result.Replace(token, strVal);
            }
        }

        // Hızlı onay/red linkleri — parametre olarak gelir; boşsa token silinir.
        result = result
            .Replace("{approveLink}", approveLink)
            .Replace("{rejectLink}",  rejectLink);

        return result;
    }

    private static int? TryGetInt(ApprovalEntityContext ctx, string field)
    {
        if (!ctx.HeaderValues.TryGetValue(field, out var v) || v is null) return null;
        return v switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            string s when int.TryParse(s, out var p) => p,
            _ => null,
        };
    }

    private async Task<EmailAttachment?> TryBuildPdfAttachmentAsync(ApprovalEntityContext ctx, CancellationToken ct)
    {
        if (_designer is null) return null;

        try
        {
            // DocLayoutRuleService.MatchAsync olmadığı için MVP: entity tipine göre
            // varsayılan layout'u Designer.ListAsync ile bul (IsDefault=true).
            // EntityTypeCode artık "Document" gibi generic code — Designer çoğunlukla
            // bu kategoride zaten layout barındırıyor; bulamazsa null PDF.
            var layouts = await _designer.ListAsync(ctx.EntityTypeCode ?? "", ct);
            var layout = layouts.FirstOrDefault();  // ilk uygun layout
            if (layout is null) return null;

            var docNumber = ctx.HeaderValues.TryGetValue("documentNumber", out var dn) ? dn?.ToString() ?? "" : "";
            var req = new DocLayoutRunRequest(
                LayoutId: layout.Id,
                DocumentId: null,
                ParamOverrides: new Dictionary<string, string>
                {
                    ["documentId"]     = ctx.EntityId ?? "",
                    ["documentNumber"] = docNumber,
                });

            var pdf = await _designer.RenderPdfAsync(req, ct);
            if (pdf is null || pdf.Length == 0) return null;

            var fileName = $"{(string.IsNullOrWhiteSpace(docNumber) ? ctx.EntityId : docNumber)}.pdf";
            return new EmailAttachment(fileName, pdf, "application/pdf");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PDF eki oluşturulamadı — atlandı.");
            return null;
        }
    }

    private sealed class NotificationNodeConfig
    {
        public string? NotificationType { get; init; }
        public string? RecipientMode { get; init; }
        public string? RecipientId { get; init; }
        public string? CustomEmail { get; init; }
        public string? CustomPhone { get; init; }
        public string? Subject { get; init; }
        public string? Body { get; init; }
        public bool AttachPdf { get; init; }
    }

    private sealed class NotifyRecipient
    {
        public required string Name { get; init; }
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public int CompanyId { get; init; }
        public string? UserId { get; init; }

        public static NotifyRecipient From(CalibraHub.Domain.Entities.UserProfile u) => new()
        {
            Name = u.FullName,
            Email = u.Email,
            Phone = u.PhoneNumber,
            CompanyId = u.CompanyId,
            UserId = u.Id.ToString(),
        };
    }
}
