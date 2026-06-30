using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.SmartBoard;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models.MailSend;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mail sablon + toplu gonderim ekrani (Faz 2 — 2026-05-29 redesign).
/// 2 bolumlu tam sayfa: sol kompozisyon, sag C-Grid alici listesi. Gonderim
/// log'u MailSendBatch + MailSendLogItem tablolarinda.
/// </summary>
[Authorize]
[Route("/MailSend")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.BulkMail)]
public sealed class MailSendController : Controller
{
    private readonly IDocDesignerService _docDesignerService;
    private readonly IDocLayoutRepository _docLayoutRepo;
    private readonly IMailTemplateRenderer _renderer;
    private readonly IEmailSender _emailSender;
    private readonly SqlServerConnectionFactory _connFactory;
    private readonly IMailSendBatchRepository _batchRepo;
    private readonly string _schema;

    public MailSendController(
        IDocDesignerService docDesignerService,
        IDocLayoutRepository docLayoutRepo,
        IMailTemplateRenderer renderer,
        IEmailSender emailSender,
        SqlServerConnectionFactory connFactory,
        IMailSendBatchRepository batchRepo,
        CalibraDatabaseOptions options)
    {
        _docDesignerService = docDesignerService;
        _docLayoutRepo = docLayoutRepo;
        _renderer = renderer;
        _emailSender = emailSender;
        _connFactory = connFactory;
        _batchRepo = batchRepo;
        _schema = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
    }

    // ── Page ──────────────────────────────────────────────────────────────────
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var board = await BuildHistoryBoardAsync(ct);
        return View(new MailSendIndexViewModel { BoardConfig = board });
    }

    // ── Compose sayfasi (3 sekmeli toplu gonderim) ───────────────────────────
    [HttpGet("Compose")]
    public IActionResult Compose() => View();

    // ── Detail tasarim onizlemesi (workspace tab full-page) ──────────────────
    // Detail sayfasindaki "Önizle" butonu bu URL'e openWorkspaceTab ile gider.
    // Tam sayfa rendered HTML doner, Ctrl+P ile yazdirilabilir.
    [HttpGet("PreviewPage/{batchId:int}")]
    public async Task<IActionResult> PreviewPage(int batchId, CancellationToken ct)
    {
        if (batchId <= 0) return NotFound();
        var companyId = ResolveCompanyId();
        var (batch, items) = await _batchRepo.GetBatchDetailAsync(batchId, companyId, ct);
        if (batch == null) return NotFound();
        try
        {
            var layout = await _docDesignerService.GetAsync(batch.LayoutId, ct);
            if (layout is null) return NotFound();
            var firstItem = items.FirstOrDefault();
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["currentDate"] = batch.SentAt.ToLocalTime().ToString("dd.MM.yyyy"),
                ["personName"]  = firstItem?.RecipientName  ?? "[Alıcı Adı]",
                ["personEmail"] = firstItem?.RecipientEmail ?? "[email]",
                ["personTitle"] = firstItem?.TitleName      ?? "[Ünvan]",
                ["contactName"] = firstItem?.ContactName    ?? "[Cari Adı]",
                ["contactCode"] = "[CARI-KODU]",
            };
            var html = _renderer.RenderHtml(layout, tokens, batch.BodyPreview);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            var msg = System.Net.WebUtility.HtmlEncode("İşlem sırasında bir hata oluştu.");
            return Content(
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>Önizleme Hatası</title>"
                + "<style>body{font-family:system-ui,sans-serif;padding:40px;color:#dc2626;background:#fef2f2;}"
                + "pre{white-space:pre-wrap;background:#fff;padding:14px;border-radius:6px;border:1px solid #fca5a5;}</style>"
                + "</head><body><h2>Önizleme oluşturulamadı</h2><pre>" + msg + "</pre></body></html>",
                "text/html; charset=utf-8");
        }
    }

    // ── Detail alici listesi (workspace tab full-page) ───────────────────────
    // Detail sayfasindaki Alici Listesi sekmesinde "Listeyi Yeni Sekmede Aç" butonu
    // bu URL'e openWorkspaceTab ile gider. Tam sayfa yazdirilabilir tablo dondurur.
    [HttpGet("RecipientsPage/{batchId:int}")]
    public async Task<IActionResult> RecipientsPage(int batchId, CancellationToken ct)
    {
        if (batchId <= 0) return NotFound();
        var companyId = ResolveCompanyId();
        var (batch, items) = await _batchRepo.GetBatchDetailAsync(batchId, companyId, ct);
        if (batch == null) return NotFound();

        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.Append("<title>Alıcı Listesi — Gönderim #").Append(batch.Id).Append("</title>");
        sb.Append("<style>");
        sb.Append("body{font-family:system-ui,-apple-system,Segoe UI,sans-serif;color:#1e293b;background:#f8fafc;margin:0;padding:24px;}");
        sb.Append(".wrap{max-width:1100px;margin:0 auto;background:#fff;border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;}");
        sb.Append(".hdr{padding:16px 22px;border-bottom:1px solid #e2e8f0;}");
        sb.Append("h1{font-size:18px;margin:0 0 4px;}.sub{font-size:12.5px;color:#64748b;}");
        sb.Append(".meta{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;padding:14px 22px;border-bottom:1px solid #e2e8f0;background:#f8fafc;}");
        sb.Append(".cell{padding:8px 10px;border:1px solid #e2e8f0;border-radius:7px;background:#fff;}");
        sb.Append(".cell .lbl{font-size:10.5px;text-transform:uppercase;letter-spacing:.05em;color:#64748b;font-weight:700;}");
        sb.Append(".cell .val{font-size:13px;font-weight:600;margin-top:2px;}");
        sb.Append("table{width:100%;border-collapse:collapse;font-size:12.5px;}");
        sb.Append("th,td{padding:9px 12px;border-bottom:1px solid #e2e8f0;text-align:left;vertical-align:top;}");
        sb.Append("th{font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:#64748b;background:#f8fafc;font-weight:700;}");
        sb.Append(".pill{display:inline-block;padding:2px 9px;border-radius:12px;font-size:10.5px;font-weight:700;text-transform:uppercase;letter-spacing:.04em;}");
        sb.Append(".sent{background:rgba(16,185,129,.18);color:#059669;}");
        sb.Append(".failed{background:rgba(239,68,68,.18);color:#dc2626;}");
        sb.Append(".other{background:rgba(148,163,184,.20);color:#475569;}");
        sb.Append(".err{color:#dc2626;font-size:11.5px;max-width:280px;word-break:break-word;}");
        sb.Append("@media print{body{padding:0;background:#fff;}.wrap{border:0;border-radius:0;}}");
        sb.Append("</style></head><body><div class=\"wrap\">");

        sb.Append("<div class=\"hdr\"><h1>Alıcı Listesi — Gönderim #").Append(batch.Id).Append("</h1>");
        sb.Append("<div class=\"sub\">").Append(System.Net.WebUtility.HtmlEncode(batch.LayoutName ?? "(şablon)"))
          .Append(" • ").Append(batch.SentAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
          .Append(" • ").Append(System.Net.WebUtility.HtmlEncode(batch.SentBy ?? "—")).Append("</div></div>");

        sb.Append("<div class=\"meta\">");
        sb.Append("<div class=\"cell\"><div class=\"lbl\">Toplam</div><div class=\"val\">").Append(batch.TotalCount).Append("</div></div>");
        sb.Append("<div class=\"cell\"><div class=\"lbl\">Gönderildi</div><div class=\"val\" style=\"color:#059669;\">").Append(batch.SentCount).Append("</div></div>");
        sb.Append("<div class=\"cell\"><div class=\"lbl\">Başarısız</div><div class=\"val\" style=\"color:")
          .Append(batch.FailCount > 0 ? "#dc2626" : "#64748b").Append(";\">").Append(batch.FailCount).Append("</div></div>");
        sb.Append("<div class=\"cell\"><div class=\"lbl\">Konu</div><div class=\"val\" style=\"font-size:12.5px;\">")
          .Append(System.Net.WebUtility.HtmlEncode(batch.Subject ?? "—")).Append("</div></div>");
        sb.Append("</div>");

        sb.Append("<table><thead><tr><th>#</th><th>Ad Soyad</th><th>Email</th><th>Ünvan</th><th>Cari</th><th>Durum</th><th>Hata</th></tr></thead><tbody>");
        var n = 0;
        foreach (var it in items)
        {
            n++;
            var statusClass = string.Equals(it.Status, "Sent", StringComparison.OrdinalIgnoreCase) ? "sent"
                            : string.Equals(it.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? "failed"
                            : "other";
            sb.Append("<tr><td>").Append(n).Append("</td>")
              .Append("<td>").Append(System.Net.WebUtility.HtmlEncode(it.RecipientName ?? "")).Append("</td>")
              .Append("<td>").Append(System.Net.WebUtility.HtmlEncode(it.RecipientEmail ?? "")).Append("</td>")
              .Append("<td>").Append(System.Net.WebUtility.HtmlEncode(it.TitleName ?? "")).Append("</td>")
              .Append("<td>").Append(System.Net.WebUtility.HtmlEncode(it.ContactName ?? "")).Append("</td>")
              .Append("<td><span class=\"pill ").Append(statusClass).Append("\">")
              .Append(System.Net.WebUtility.HtmlEncode(it.Status ?? "—")).Append("</span></td>")
              .Append("<td class=\"err\">").Append(System.Net.WebUtility.HtmlEncode(it.ErrorMessage ?? "")).Append("</td>")
              .Append("</tr>");
        }
        if (n == 0)
            sb.Append("<tr><td colspan=\"7\" style=\"text-align:center;padding:24px;color:#64748b;\">Log satırı yok.</td></tr>");
        sb.Append("</tbody></table></div></body></html>");

        return Content(sb.ToString(), "text/html; charset=utf-8");
    }

    // ── Detail sayfasi (Compose ile ayni 3-sekmeli layout, read-only) ────────
    // SmartCard tiklayinca buraya navigate eder.
    [HttpGet("Detail")]
    public async Task<IActionResult> Detail([FromQuery] int batchId, CancellationToken ct)
    {
        if (batchId <= 0) return RedirectToAction(nameof(Index));
        var companyId = ResolveCompanyId();
        var (batch, items) = await _batchRepo.GetBatchDetailAsync(batchId, companyId, ct);
        if (batch == null) return RedirectToAction(nameof(Index));

        string? previewHtml = null;
        string? layoutDescription = null;
        try
        {
            var layout = await _docDesignerService.GetAsync(batch.LayoutId, ct);
            if (layout is not null)
            {
                layoutDescription = layout.Description;
                var firstItem = items.FirstOrDefault();
                var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["currentDate"] = batch.SentAt.ToLocalTime().ToString("dd.MM.yyyy"),
                    ["personName"]  = firstItem?.RecipientName  ?? "[Alıcı Adı]",
                    ["personEmail"] = firstItem?.RecipientEmail ?? "[email]",
                    ["personTitle"] = firstItem?.TitleName      ?? "[Ünvan]",
                    ["contactName"] = firstItem?.ContactName    ?? "[Cari Adı]",
                    ["contactCode"] = "[CARI-KODU]",
                };
                previewHtml = _renderer.RenderHtml(layout, tokens, batch.BodyPreview);
            }
        }
        catch { /* render hatasi gosterimi engellemesin */ }

        ViewData["Title"] = $"Gönderim #{batch.Id}";
        return View(new MailSendDetailViewModel
        {
            BatchId            = batch.Id,
            LayoutName         = batch.LayoutName,
            LayoutDescription  = layoutDescription,
            Subject            = batch.Subject,
            BodyPreview        = batch.BodyPreview,
            TotalCount         = batch.TotalCount,
            SentCount          = batch.SentCount,
            FailCount          = batch.FailCount,
            SentBy             = batch.SentBy,
            SentAt             = batch.SentAt,
            TitleNames         = SafeJsonArray(batch.TitleNamesJson),
            PreviewHtml        = previewHtml,
            Items              = items.Select(i => new MailSendDetailViewModel.RecipientRow(
                                    i.Id, i.RecipientName, i.RecipientEmail, i.TitleName,
                                    i.ContactName, i.Status, i.ErrorMessage, i.SentAt)).ToArray(),
        });
    }

    // ── API: Sablon listesi ───────────────────────────────────────────────────
    // 2026-05-20: UI'da Cikti Turu dropdown'i kaldirildi; yeni bayrak UseAsMailTemplate.
    // Eski OutputFormat='email' kayitlari da kabul edilir (backward-compat). Yeni
    // tasarimlar UseAsMailTemplate=1 ile isaretlenir.
    [HttpGet("Templates")]
    public async Task<IActionResult> Templates(CancellationToken ct)
    {
        await using var conn = await _connFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[Description],[IsDefault],[UpdatedAt],[DefaultSubject],[DefaultBody]
            FROM [{_schema}].[DocLayout]
            WHERE [IsActive] = 1
              AND (
                    ISNULL([UseAsMailTemplate], 0) = 1
                 OR [OutputFormat] = N'email'
              )
            ORDER BY [IsDefault] DESC, [Name];";

        var list = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new
            {
                id             = reader.GetInt32(0),
                code           = reader.GetString(1),
                name           = reader.GetString(2),
                description    = reader.IsDBNull(3) ? null : reader.GetString(3),
                isDefault      = reader.GetBoolean(4),
                updatedAt      = reader.GetDateTime(5),
                defaultSubject = reader.IsDBNull(6) ? null : reader.GetString(6),
                defaultBody    = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }
        return Json(list);
    }

    // ── API: View tabanli varsayilanlari coz ─────────────────────────────────
    // Compose ekraninda sablon secilince cagrilir. Sablonda DefaultsViewName tanimliysa
    // view'in ilk satirindan Subject + Body kolonlari alinir.
    // Guvenlik:
    //  - View adi sadece [A-Za-z0-9_.] karakterleri (DDL injection korumasi)
    //  - Kolon adlari sadece [A-Za-z0-9_]
    //  - WHERE serbest ama bracketed identifier'lardan kacinilmalı; runtime'da hata olursa null doner
    [HttpGet("ResolveDefaults")]
    public async Task<IActionResult> ResolveDefaults([FromQuery] int layoutId, CancellationToken ct)
    {
        if (layoutId <= 0) return Json(new { ok = false, subject = (string?)null, body = (string?)null });
        var layout = await _docDesignerService.GetAsync(layoutId, ct);
        if (layout == null) return Json(new { ok = false, subject = (string?)null, body = (string?)null });

        // 1. Yeni view bagimi yoksa eski DefaultSubject/DefaultBody'ye fallback
        if (string.IsNullOrWhiteSpace(layout.DefaultsViewName))
        {
            return Json(new
            {
                ok = true,
                source = "legacy",
                subject = layout.DefaultSubject,
                body = layout.DefaultBody,
            });
        }

        // 2. Identifier validation — sql injection korumasi
        bool ValidIdent(string s) => !string.IsNullOrWhiteSpace(s)
                                  && s.Length <= 128
                                  && s.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');
        if (!ValidIdent(layout.DefaultsViewName)
            || (!string.IsNullOrEmpty(layout.DefaultsSubjectColumn) && !ValidIdent(layout.DefaultsSubjectColumn))
            || (!string.IsNullOrEmpty(layout.DefaultsBodyColumn)    && !ValidIdent(layout.DefaultsBodyColumn)))
        {
            return Json(new { ok = false, error = "Geçersiz view veya kolon adı." });
        }

        // 3. WHERE — basit blacklist (DML/DDL keyword'leri reddet)
        var where = layout.DefaultsWhere ?? string.Empty;
        var lowerWhere = where.ToLowerInvariant();
        string[] blocked = { ";", "--", "/*", "*/", "xp_", "exec ", "execute ", "drop ", "alter ", "create ", "insert ", "update ", "delete ", "merge ", "truncate " };
        foreach (var bad in blocked)
        {
            if (lowerWhere.Contains(bad))
                return Json(new { ok = false, error = "WHERE şartı güvenlik kontrolünden geçemedi." });
        }

        var subjectCol = string.IsNullOrEmpty(layout.DefaultsSubjectColumn) ? null : layout.DefaultsSubjectColumn;
        var bodyCol    = string.IsNullOrEmpty(layout.DefaultsBodyColumn)    ? null : layout.DefaultsBodyColumn;
        var selectCols = new List<string>();
        if (subjectCol != null) selectCols.Add($"[{subjectCol}] AS __subject");
        if (bodyCol    != null) selectCols.Add($"[{bodyCol}] AS __body");
        if (selectCols.Count == 0)
            return Json(new { ok = true, source = "view-empty", subject = (string?)null, body = (string?)null });

        // schema.view veya schema.[view] formati — kullanici girdi
        var viewRef = string.Join(".", layout.DefaultsViewName.Split('.').Select(p => "[" + p + "]"));
        var sql = $"SELECT TOP 1 {string.Join(", ", selectCols)} FROM {viewRef}";
        if (!string.IsNullOrWhiteSpace(where)) sql += $" WHERE {where}";
        sql += ";";

        try
        {
            await using var conn = await _connFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 10;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? subject = null, body = null;
            if (await r.ReadAsync(ct))
            {
                if (subjectCol != null)
                {
                    var idx = r.GetOrdinal("__subject");
                    subject = r.IsDBNull(idx) ? null : r.GetValue(idx)?.ToString();
                }
                if (bodyCol != null)
                {
                    var idx = r.GetOrdinal("__body");
                    body = r.IsDBNull(idx) ? null : r.GetValue(idx)?.ToString();
                }
            }
            return Json(new { ok = true, source = "view", subject, body });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "View sorgusu hatası: " + "İşlem sırasında bir hata oluştu." });
        }
    }

    // ── API: PDF sablon listesi (belge eki secimi) ───────────────────────────
    // Belge bagindan mail gonderimde "Belge PDF'i ekle" seçeneği için dropdown.
    [HttpGet("PdfTemplates")]
    public async Task<IActionResult> PdfTemplates([FromQuery] string? docType, CancellationToken ct)
    {
        await using var conn = await _connFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[IsDefault],[DocType]
            FROM [{_schema}].[DocLayout]
            WHERE [IsActive] = 1 AND [OutputFormat] = N'pdf'
              AND (@DocType IS NULL OR [DocType] = @DocType)
            ORDER BY [IsDefault] DESC, [Name];";
        cmd.Parameters.Add(new SqlParameter("@DocType", System.Data.SqlDbType.NVarChar, 60)
            { Value = (object?)docType ?? DBNull.Value });

        var list = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                id        = r.GetInt32(0),
                code      = r.GetString(1),
                name      = r.GetString(2),
                isDefault = r.GetBoolean(3),
                docType   = r.IsDBNull(4) ? null : r.GetString(4),
            });
        }
        return Json(list);
    }

    // ── API: Unvan listesi (toplu mail recipient filtre) ─────────────────────
    [HttpGet("Titles")]
    public async Task<IActionResult> Titles(CancellationToken ct)
    {
        var companyId = ResolveCompanyId();
        await using var conn = await _connFactory.OpenConnectionAsync(ct);

        // ContactPersonTitle listesi
        var list = new List<object>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT t.[Id], t.[Name], t.[SortOrder], t.[IsSystem],
                       ISNULL(cnt.UsageCount, 0) AS UsageCount
                FROM [{_schema}].[ContactPersonTitle] t
                OUTER APPLY (
                    SELECT COUNT(*) AS UsageCount
                    FROM [{_schema}].[ContactPerson] cp
                    INNER JOIN [{_schema}].[Contact] c ON c.[Id] = cp.[ContactId]
                    WHERE cp.[TitleId] = t.[Id]
                      AND cp.[IsActive] = 1
                      AND c.[IsActive] = 1
                      AND cp.[Email] IS NOT NULL AND LTRIM(RTRIM(cp.[Email])) <> N''
                ) cnt
                WHERE t.[IsActive] = 1
                ORDER BY t.[SortOrder], t.[Name];";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new
                {
                    id         = r.GetInt32(0),
                    name       = r.GetString(1),
                    sortOrder  = r.GetInt32(2),
                    isSystem   = r.GetBoolean(3),
                    usageCount = r.GetInt32(4),
                });
            }
        }

        // Sentetik "Kurumsal İletişim" satiri — Contact.Email (cari kurumsal mail) olanlari sayar.
        // Bu virtual title secilince LoadRecipientsAsync Contact tablosundan recipient turetir.
        var corporateCount = 0;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT COUNT(*) FROM [{_schema}].[Contact]
                WHERE [IsActive] = 1
                  AND [Email] IS NOT NULL AND LTRIM(RTRIM([Email])) <> N''
                  AND (@CompanyId = 0 OR [CompanyId] = @CompanyId);";
            cmd.Parameters.Add(new SqlParameter("@CompanyId", System.Data.SqlDbType.Int) { Value = companyId });
            var v = await cmd.ExecuteScalarAsync(ct);
            corporateCount = v is int i ? i : 0;
        }
        // En basa koy — kullaniciya en uste gozuk
        list.Insert(0, new
        {
            id         = VIRTUAL_CORPORATE_TITLE_ID,
            name       = "Kurumsal İletişim",
            sortOrder  = -1,
            isSystem   = true,
            usageCount = corporateCount,
        });

        return Json(list);
    }

    // ── API: Title'lara gore recipient listesi (legacy + onizleme icin) ──────
    // 2026-05-20: q (arama) + top (limit, default 1000) parametreleri eklendi —
    // "Kurumsal Iletisim" gibi 10K+ cari iceren title'larda donmek zorunda kalmadan
    // ilk N kaydi hizli doner. Frontend top'a ulasildiginda kullaniciyi uyarir.
    [HttpGet("RecipientsByTitle")]
    public async Task<IActionResult> RecipientsByTitle(
        [FromQuery] string? titleIds,
        [FromQuery] string? q,
        [FromQuery] int top = 1000,
        [FromQuery] int offset = 0,
        [FromQuery] int g1 = 0, [FromQuery] int g2 = 0, [FromQuery] int g3 = 0,
        [FromQuery] int g4 = 0, [FromQuery] int g5 = 0,
        CancellationToken ct = default)
    {
        var ids = ParseIds(titleIds);
        if (ids.Length == 0)
            return Json(new { ok = true, data = Array.Empty<object>(), count = 0, truncated = false, offset = 0 });

        // Limit guard: 1..5000 araliginda zorla
        if (top <= 0) top = 1000;
        if (top > 5000) top = 5000;
        if (offset < 0) offset = 0;

        var groups = new[] { g1, g2, g3, g4, g5 };
        // top+1 ile bir sonraki page olup olmadigini test ediyoruz (truncated bayrak)
        var recipients = await LoadRecipientsAsync(ids, q, top + 1, offset, groups, ct);
        var hasMore = recipients.Count > top;
        if (hasMore) recipients = recipients.GetRange(0, top);

        var data = recipients.Select(rcp => new
        {
            contactPersonId = rcp.Id,
            fullName        = rcp.FullName,
            title           = rcp.Title,
            email           = rcp.Email,
            phone           = rcp.Phone,
            contactId       = rcp.ContactId,
            accountCode     = rcp.ContactCode,
            accountTitle    = rcp.ContactTitle,
        }).ToArray();
        // truncated alani backward-compat icin korundu (eski frontend kullanir);
        // hasMore daha net anlam tasir — yeni infinite-scroll akisi bu alani okur.
        return Json(new { ok = true, data, count = data.Length, truncated = hasMore, hasMore, limit = top, offset });
    }

    // ── API: Filtreye uyan TUM recipient id'leri (limit yok) ─────────────────
    // 2026-05-20: "Tumunu Haric Tut" akisi icin — gorunur 1000 disindaki
    // alicilar da haric tutulabilsin diye sadece id listesi doner. Metadata yok,
    // payload kucuk (10K kayit = ~80KB). TOP yoktur — tum filtreyi gezer.
    [HttpGet("RecipientIdsByTitle")]
    public async Task<IActionResult> RecipientIdsByTitle(
        [FromQuery] string? titleIds,
        [FromQuery] string? q,
        [FromQuery] int g1 = 0, [FromQuery] int g2 = 0, [FromQuery] int g3 = 0,
        [FromQuery] int g4 = 0, [FromQuery] int g5 = 0,
        CancellationToken ct = default)
    {
        var ids = ParseIds(titleIds);
        if (ids.Length == 0)
            return Json(new { ok = true, ids = Array.Empty<int>(), count = 0 });

        var groups = new[] { g1, g2, g3, g4, g5 };
        // Limit yok — int.MaxValue ile LoadRecipientsAsync tum kayitlari doner
        var recipients = await LoadRecipientsAsync(ids, q, int.MaxValue, groups, ct);
        var rcpIds = recipients.Select(r => r.Id).ToArray();
        return Json(new { ok = true, ids = rcpIds, count = rcpIds.Length });
    }

    // ── API: Alici C-Grid board config ───────────────────────────────────────
    // 2026-05-29: Sag panele basilan SmartBoard config. Title secimi degistikce
    // JS bu endpoint'i tekrar cagirir, mountSmartBoard re-mount eder.
    [HttpGet("RecipientBoard")]
    public async Task<IActionResult> RecipientBoard([FromQuery] string? titleIds, CancellationToken ct)
    {
        var ids = ParseIds(titleIds);
        var recipients = ids.Length == 0
            ? new List<RecipientRow>()
            : (await LoadRecipientsAsync(ids, ct)).ToList();

        var board = SmartBoard.For(recipients)
            .WithBoardKey("mail-recipients")
            .WithTitle("Alıcılar", subtitle: $"{recipients.Count} kişi")
            .WithIcon("Mail", "indigo")
            .WithSearchPlaceholder("Kişi / cari / email ara…")
            .WithEmptyText(ids.Length == 0
                ? "Önce ünvan seçin"
                : "Seçili ünvan(lar) için email adresi olan kişi yok")
            .MapEntities(rcp =>
            {
                var b = SmartBoardEntity.For(rcp.Id, rcp.FullName ?? "(isimsiz)")
                    .WithSubtitle(rcp.Email)
                    .WithDescription(string.IsNullOrWhiteSpace(rcp.Title) ? "" : rcp.Title);
                if (!string.IsNullOrWhiteSpace(rcp.ContactTitle))
                    b.AddTextWidget("w_contact", "Cari", rcp.ContactTitle, color: "indigo");
                if (!string.IsNullOrWhiteSpace(rcp.Phone))
                    b.AddTextWidget("w_phone", "Telefon", rcp.Phone, color: "slate");
                return b;
            })
            .Build();
        return Json(board);
    }

    // ── API: Gecmis batch listesi (C-Grid board) ─────────────────────────────
    [HttpGet("HistoryBoard")]
    public async Task<IActionResult> HistoryBoard(CancellationToken ct)
    {
        var board = await BuildHistoryBoardAsync(ct);
        return Json(board);
    }

    // ── API: Batch sil ────────────────────────────────────────────────────────
    // SmartCard "Sil" butonundan POST edilir. Item satirlari + batch baslik kalici silinir.
    [HttpPost("DeleteBatch")]
    public async Task<IActionResult> DeleteBatch([FromQuery] int id, CancellationToken ct)
    {
        if (id <= 0) return Json(new { ok = false, message = "Gecersiz batch id." });
        var companyId = ResolveCompanyId();
        try
        {
            await _batchRepo.DeleteBatchAsync(id, companyId, ct);
            return Json(new { ok = true, message = "Gönderim silindi." });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    private async Task<object> BuildHistoryBoardAsync(CancellationToken ct)
    {
        var companyId = ResolveCompanyId();
        var batches = await _batchRepo.GetRecentBatchesAsync(companyId, 200, ct);

        return SmartBoard.For(batches)
            .WithBoardKey("mail-history")
            .WithTitle("Gönderim Geçmişi", subtitle: $"{batches.Count} batch")
            .WithIcon("History", "violet")
            .WithRefreshUrl("/MailSend/HistoryBoard")
            .WithSearchPlaceholder("Şablon / konu ara…")
            .WithEmptyText("Henüz toplu mail gönderimi yapılmamış")
            .AddHeaderAction("new", "Yeni Toplu Gönderim", "Plus", "/MailSend/Compose")
            .MapEntities(b =>
            {
                var e = SmartBoardEntity.For(b.Id, b.LayoutName ?? "(şablon)")
                    .WithSubtitle($"#{b.Id}")
                    .WithDescription(b.Subject ?? "")
                    .AddNumericWidget("w_total",  "Toplam",     b.TotalCount.ToString(), color: "indigo")
                    .AddNumericWidget("w_sent",   "Gönderildi", b.SentCount.ToString(),  color: "emerald")
                    .AddNumericWidget("w_fail",   "Başarısız",  b.FailCount.ToString(),  color: b.FailCount > 0 ? "rose" : "slate")
                    .AddTextWidget   ("w_when",   "Tarih",      b.SentAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), color: "slate")
                    // Karta tiklayinca Compose ile ayni layout'taki Detail sayfasi acilir.
                    .WithPrimaryAction("Detay", "Eye", $"/MailSend/Detail?batchId={b.Id}", color: "violet", hideButton: true)
                    // Sol baslarda kucuk cop ikonu — SmartCard.handleSecondary CalibraAlert.confirm acar
                    .WithSecondaryAction(
                        label:    "Sil",
                        icon:     "Trash2",
                        apiUrl:   $"/MailSend/DeleteBatch?id={b.Id}",
                        apiMethod:"POST",
                        confirm:  $"#{b.Id} numaralı gönderimi (ve tüm log satırlarını) silmek istediğinize emin misiniz?");
                return e;
            })
            .Build();
    }

    // ── API: Tek batch detay (modal) ─────────────────────────────────────────
    [HttpGet("HistoryDetail")]
    public async Task<IActionResult> HistoryDetail([FromQuery] int batchId, CancellationToken ct)
    {
        if (batchId <= 0) return Json(new { ok = false, error = "batchId zorunlu." });
        var companyId = ResolveCompanyId();
        var (batch, items) = await _batchRepo.GetBatchDetailAsync(batchId, companyId, ct);
        if (batch == null) return Json(new { ok = false, error = "Batch bulunamadı." });

        // Layout'u tipik token'larla render et — gondrim aninda kullanilan body + ilk
        // alicinin bilgileri uzerinden. BodyPreview 500 char kesik olsa da template
        // structure'i goruluyor; gercek gonderilen icerikle ayni dizilim.
        string? previewHtml = null;
        try
        {
            var layout = await _docDesignerService.GetAsync(batch.LayoutId, ct);
            if (layout is not null)
            {
                var firstItem = items.FirstOrDefault();
                var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["currentDate"] = batch.SentAt.ToLocalTime().ToString("dd.MM.yyyy"),
                    ["personName"]  = firstItem?.RecipientName  ?? "[Alıcı Adı]",
                    ["personEmail"] = firstItem?.RecipientEmail ?? "[email]",
                    ["personTitle"] = firstItem?.TitleName      ?? "[Ünvan]",
                    ["contactName"] = firstItem?.ContactName    ?? "[Cari Adı]",
                    ["contactCode"] = "[CARI-KODU]",
                };
                previewHtml = _renderer.RenderHtml(layout, tokens, batch.BodyPreview);
            }
        }
        catch
        {
            // Preview render hatasi modal'i bozmasin — sadece preview gosterilmez.
        }

        return Json(new
        {
            ok = true,
            batch = new
            {
                id          = batch.Id,
                layoutId    = batch.LayoutId,
                layoutName  = batch.LayoutName,
                subject     = batch.Subject,
                bodyPreview = batch.BodyPreview,
                totalCount  = batch.TotalCount,
                sentCount   = batch.SentCount,
                failCount   = batch.FailCount,
                sentBy      = batch.SentBy,
                sentAt      = batch.SentAt,
                titleNames  = SafeJsonArray(batch.TitleNamesJson),
                previewHtml,
            },
            items = items.Select(i => new
            {
                id              = i.Id,
                recipientName   = i.RecipientName,
                recipientEmail  = i.RecipientEmail,
                titleName       = i.TitleName,
                contactName     = i.ContactName,
                status          = i.Status,
                errorMessage    = i.ErrorMessage,
                sentAt          = i.SentAt,
            }).ToArray(),
        });
    }

    // ── API: Toplu mail gonder ───────────────────────────────────────────────
    public sealed record SendBulkRequest(int LayoutId, int[]? TitleIds, int[]? ContactPersonIds, string? Subject, string? Body);

    // Multipart/form-data: dosya ek'lerini de kabul eder.
    // Toplam istek boyutu 50 MB ile sinirli (kuyrukta tikanma + SMTP relay limitleri icin).
    [HttpPost("SendBulk")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> SendBulk(
        [FromForm] int layoutId,
        [FromForm] int[]? titleIds,
        [FromForm] int[]? contactPersonIds,
        [FromForm(Name = "subject")] string? subjectInput,
        [FromForm(Name = "body")] string? bodyInput,
        [FromForm(Name = "attachments")] List<IFormFile>? uploadedFiles,
        // 2026-05-31: Belge bagindan mail gonderim — formdaki belge PDF'i ek olarak yollanir.
        [FromForm(Name = "documentPdfLayoutId")] int? documentPdfLayoutId,
        [FromForm(Name = "documentId")] int? documentId,
        [FromForm(Name = "documentPdfFileName")] string? documentPdfFileName,
        CancellationToken ct)
    {
        var req = new SendBulkRequest(layoutId, titleIds, contactPersonIds, subjectInput, bodyInput);
        if (req.LayoutId <= 0)
            return Json(new { ok = false, message = "LayoutId zorunlu." });
        if ((req.TitleIds == null || req.TitleIds.Length == 0)
            && (req.ContactPersonIds == null || req.ContactPersonIds.Length == 0))
            return Json(new { ok = false, message = "En az bir ünvan veya kişi seçilmelidir." });

        // Dosya eklerini bellekte oku (toplam boyut + sayı sınırı)
        var emailAttachments = new List<EmailAttachment>();
        if (uploadedFiles is { Count: > 0 })
        {
            const int maxFiles = 10;
            const long maxTotalBytes = 25L * 1024 * 1024; // 25 MB net (SMTP relay'lerin tipik base64 limiti)
            if (uploadedFiles.Count > maxFiles)
                return Json(new { ok = false, message = $"En fazla {maxFiles} dosya eklenebilir." });

            long total = 0;
            foreach (var f in uploadedFiles.Where(x => x.Length > 0))
            {
                total += f.Length;
                if (total > maxTotalBytes)
                    return Json(new { ok = false, message = "Toplam ek boyutu 25 MB'i aşamaz." });

                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                emailAttachments.Add(new EmailAttachment(
                    FileName:    SanitizeFileName(f.FileName),
                    Content:     ms.ToArray(),
                    ContentType: string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType));
            }
        }

        // Belge PDF'i ek olarak — tum aliciler ayni PDF'i alir (klasik "teklif/siparis PDF olarak yolla").
        // Toplam ek boyutu kontrolu icin uploadedFiles ile birlikte sayilir.
        if (documentPdfLayoutId is > 0 && documentId is > 0)
        {
            try
            {
                var pdfBytes = await _docDesignerService.RenderPdfAsync(
                    new DocLayoutRunRequest(documentPdfLayoutId.Value, documentId, null), ct);
                if (pdfBytes is { Length: > 0 })
                {
                    var pdfName = SanitizeFileName(string.IsNullOrWhiteSpace(documentPdfFileName)
                        ? $"belge_{documentId}.pdf"
                        : (documentPdfFileName!.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                            ? documentPdfFileName
                            : documentPdfFileName + ".pdf"));
                    emailAttachments.Add(new EmailAttachment(
                        FileName:    pdfName,
                        Content:     pdfBytes,
                        ContentType: "application/pdf"));
                }
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = "Belge PDF üretilemedi: " + "İşlem sırasında bir hata oluştu." });
            }
        }

        var layout = await _docDesignerService.GetAsync(req.LayoutId, ct);
        if (layout == null)
            return Json(new { ok = false, message = "Şablon bulunamadı." });
        // 2026-05-20: UseAsMailTemplate bayragi VEYA legacy OutputFormat=email kabul.
        var isMailLayout = layout.UseAsMailTemplate
                        || string.Equals(layout.OutputFormat, "email", StringComparison.OrdinalIgnoreCase);
        if (!isMailLayout)
            return Json(new { ok = false, message = "Bu dizayn mail şablonu olarak işaretlenmemiş. Belge Tasarımcısı'nda 'Mail şablonu olarak da kullan' seçeneğini açın." });

        var ids = (req.TitleIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToArray();

        // ID-tabanli eslestirme: client explicit kisi listesi gondermisse onu kullan;
        // yoksa backward-compat olarak title -> recipient turetme.
        List<RecipientRow> recipients;
        if (req.ContactPersonIds != null && req.ContactPersonIds.Length > 0)
        {
            var personIds = req.ContactPersonIds.Where(x => x > 0).Distinct().ToArray();
            recipients = await LoadRecipientsByPersonIdsAsync(personIds, ct);
        }
        else
        {
            recipients = await LoadRecipientsAsync(ids, ct);
        }

        if (recipients.Count == 0)
            return Json(new { ok = false, message = "Seçili ünvan(lar) için mail adresi olan kişi bulunamadı." });

        var subject = string.IsNullOrWhiteSpace(req.Subject) ? layout.Name : req.Subject!.Trim();
        var companyId = ResolveCompanyId();

        // Title isimlerini denormalize tut (sablon/unvan silinirse gecmis bozulmasin)
        var titleNames = await LoadTitleNamesAsync(ids, ct);

        // 1. Batch baslik kaydi
        var batchId = await _batchRepo.CreateBatchAsync(new MailSendBatch
        {
            LayoutId       = layout.Id,
            LayoutName     = layout.Name,
            Subject        = subject,
            BodyPreview    = string.IsNullOrEmpty(req.Body) ? null : req.Body.Substring(0, Math.Min(500, req.Body.Length)),
            TitleIdsJson   = JsonSerializer.Serialize(ids),
            TitleNamesJson = JsonSerializer.Serialize(titleNames),
            TotalCount     = recipients.Count,
            SentCount      = 0,
            FailCount      = 0,
            SentBy         = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "system",
            CompanyId      = companyId,
        }, ct);

        int success = 0, fail = 0;
        var errors = new List<string>();

        foreach (var rcp in recipients)
        {
            ct.ThrowIfCancellationRequested();

            // 1) ONCE LogItem olustur — Id al. DocDesigner render'a bu Id
            // @DocumentId parametresi olarak gecirilir. View'lar WHERE Id = @DocumentId
            // ile kisisellestirilmis veri doner. Bu kontrat sayesinde Document tablosuna
            // hic dokunulmadan mail metadata snapshot'i MailSendLogItem'da yasar.
            int logItemId;
            try
            {
                logItemId = await _batchRepo.AddItemAsync(new MailSendLogItem
                {
                    BatchId         = batchId,
                    ContactPersonId = rcp.Id,
                    RecipientName   = rcp.FullName,
                    RecipientEmail  = rcp.Email ?? string.Empty,
                    TitleName       = rcp.Title,
                    ContactName     = rcp.ContactTitle,
                    Status          = "Queued",
                }, ct);
            }
            catch (Exception ex)
            {
                fail++;
                errors.Add($"{rcp.Email}: LogItem olusturulamadi: {"İşlem sırasında bir hata oluştu."}");
                continue;
            }

            string status = "Failed";
            string? errMsg = null;
            DateTime? sentAt = null;

            try
            {
                // 2) DocDesigner ile HTML render — view'lara @DocumentId = logItemId
                // gecer. Legacy token replacement (currentDate, personName vs.) layout
                // HTML uretildikten sonra final pass ile uygulanir; eski dizaynlar da
                // calismaya devam eder.
                string html;
                try
                {
                    html = await _docDesignerService.RenderHtmlPreviewAsync(
                        new DocLayoutRunRequest(req.LayoutId, logItemId, null), ct);
                }
                catch (Exception renderEx)
                {
                    throw new InvalidOperationException(
                        "Mail govdesi render edilemedi: " + renderEx.Message, renderEx);
                }

                // Legacy token fallback — dizaynda hala {{personName}} gibi
                // placeholder kullanan elementler varsa replace edilir.
                var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["currentDate"]   = DateTime.Now.ToString("dd.MM.yyyy"),
                    ["contactName"]   = rcp.ContactTitle ?? string.Empty,
                    ["contactCode"]   = rcp.ContactCode ?? string.Empty,
                    ["personName"]    = rcp.FullName ?? string.Empty,
                    ["personTitle"]   = rcp.Title ?? string.Empty,
                    ["personEmail"]   = rcp.Email ?? string.Empty,
                    ["userBody"]      = req.Body ?? string.Empty,
                };
                foreach (var kv in tokens)
                    html = html.Replace("{{" + kv.Key + "}}", kv.Value);

                // 3) SMTP gonder
                var result = await _emailSender.SendAsync(
                    companyId, new[] { rcp.Email }, subject, html,
                    attachments: emailAttachments.Count > 0 ? emailAttachments : null,
                    ct, isHtml: true);
                if (result.Status == EmailStatus.Sent)
                {
                    success++;
                    status = "Sent";
                    sentAt = DateTime.UtcNow;
                }
                else
                {
                    fail++;
                    errMsg = result.Message ?? "Bilinmeyen";
                    errors.Add($"{rcp.Email}: {errMsg}");
                }
            }
            catch (Exception ex)
            {
                fail++;
                errMsg = "İşlem sırasında bir hata oluştu.";
                errors.Add($"{rcp.Email}: {"İşlem sırasında bir hata oluştu."}");
            }

            // 4) LogItem status guncelle (Sent / Failed + sentAt + error)
            try
            {
                await _batchRepo.UpdateItemStatusAsync(logItemId, status, errMsg, sentAt, ct);
            }
            catch { /* log guncellemesi gondrim akisini durdurmasin */ }
        }

        // 3. Batch sayim guncelle
        try { await _batchRepo.UpdateBatchCountsAsync(batchId, success, fail, ct); }
        catch { /* sayim guncellemesi gondrim akisini durdurmasin */ }

        return Json(new
        {
            ok = fail == 0,
            batchId,
            sentCount = success,
            failCount = fail,
            totalCount = recipients.Count,
            message = fail == 0
                ? $"{success} kişiye mail gönderildi."
                : $"{success} gönderildi, {fail} başarısız.",
            errors = fail == 0 ? null : errors.Take(20).ToArray(),
        });
    }

    // ── API: Cari (Contact) arama ─────────────────────────────────────────────
    [HttpGet("Contacts")]
    public async Task<IActionResult> Contacts([FromQuery] string? search, CancellationToken ct)
    {
        var companyId = ResolveCompanyId();
        await using var conn = await _connFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 50 [Id], [AccountCode], [AccountTitle], [Email]
            FROM [{_schema}].[Contact]
            WHERE [IsActive] = 1
              AND (@CompanyId = 0 OR [CompanyId] = @CompanyId)
              AND ([Email] IS NOT NULL AND LTRIM(RTRIM([Email])) <> N'')
              AND (@Search IS NULL OR @Search = N'' OR
                   [AccountTitle] LIKE N'%' + @Search + N'%' OR
                   [AccountCode]  LIKE N'%' + @Search + N'%' OR
                   [Email]        LIKE N'%' + @Search + N'%')
            ORDER BY [AccountTitle];";
        cmd.Parameters.Add(new SqlParameter("@CompanyId", System.Data.SqlDbType.Int) { Value = companyId });
        cmd.Parameters.Add(new SqlParameter("@Search", System.Data.SqlDbType.NVarChar, 200)
            { Value = (object?)search ?? DBNull.Value });

        var list = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new
            {
                id           = reader.GetInt32(0),
                accountCode  = reader.IsDBNull(1) ? null : reader.GetString(1),
                accountTitle = reader.IsDBNull(2) ? null : reader.GetString(2),
                email        = reader.IsDBNull(3) ? null : reader.GetString(3),
            });
        }
        return Json(list);
    }

    public sealed record PreviewRequest(int LayoutId, int ContactId, string? Subject, string? Body);

    [HttpPost("Preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest req, CancellationToken ct)
    {
        if (req.LayoutId <= 0)
            return BadRequest(new { ok = false, error = "LayoutId zorunlu." });

        var (layout, contact, error) = await LoadLayoutAndContactAsync(req.LayoutId, req.ContactId, ct);
        if (error != null)
            return BadRequest(new { ok = false, error });

        var tokens = BuildTokens(contact);
        var html = _renderer.RenderHtml(layout!, tokens, req.Body);
        return Content(html, "text/html; charset=utf-8");
    }

    public sealed record SendRequest(int LayoutId, int ContactId, string? Subject, string? Body);

    [HttpPost("Send")]
    public async Task<IActionResult> Send([FromBody] SendRequest req, CancellationToken ct)
    {
        if (req.LayoutId <= 0)
            return Json(new { ok = false, message = "LayoutId zorunlu." });

        var (layout, contact, error) = await LoadLayoutAndContactAsync(req.LayoutId, req.ContactId, ct);
        if (error != null)
            return Json(new { ok = false, message = error });

        if (contact == null || string.IsNullOrWhiteSpace(contact.Value.Email))
            return Json(new { ok = false, message = "Secili carinin email adresi yok." });

        var subject = string.IsNullOrWhiteSpace(req.Subject) ? layout!.Name : req.Subject!;
        var tokens = BuildTokens(contact);
        var html = _renderer.RenderHtml(layout!, tokens, req.Body);

        var companyId = ResolveCompanyId();
        var result = await _emailSender.SendAsync(
            companyId,
            new[] { contact.Value.Email! },
            subject,
            html,
            attachments: null,
            ct,
            isHtml: true);

        return Json(new
        {
            ok = result.Status == EmailStatus.Sent,
            message = result.Message ?? (result.Status == EmailStatus.Sent ? "Mail gonderildi." : "Bilinmeyen hata.")
        });
    }

    // ── Internals ─────────────────────────────────────────────────────────────
    private sealed record RecipientRow(
        int Id, string FullName, string Title, string Email, string? Phone,
        int ContactId, string ContactCode, string ContactTitle);

    /// <summary>
    /// 0 reddedilir; pozitif TitleId'ler kabul edilir; -1 sentetik "Kurumsal İletişim"
    /// (Contact.Email) virtual title id'si olarak gecerli.
    /// </summary>
    private const int VIRTUAL_CORPORATE_TITLE_ID = -1;
    private static int[] ParseIds(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0 || v == VIRTUAL_CORPORATE_TITLE_ID)
            .Distinct()
            .ToArray();

    // Backward-compat overload (RecipientBoard, BulkSend gibi yerler limit'siz cagiriyor).
    private Task<List<RecipientRow>> LoadRecipientsAsync(int[] ids, CancellationToken ct)
        => LoadRecipientsAsync(ids, search: null, top: int.MaxValue, offset: 0, cardGroupIds: null, ct);

    // Mevcut callers icin 5-param overload (offset eklenmeden once cagiranlar)
    private Task<List<RecipientRow>> LoadRecipientsAsync(
        int[] ids, string? search, int top, int[]? cardGroupIds, CancellationToken ct)
        => LoadRecipientsAsync(ids, search, top, offset: 0, cardGroupIds, ct);

    /// <summary>
    /// Alici listesini yukler.
    /// <paramref name="search"/> verilirse cari adi/kodu veya kisi adi/email uzerinde
    /// sargable LIKE filtresi uygulanir.
    /// <paramref name="top"/> SQL TOP olarak gecer — buyuk Contact tablosunda full-scan'i onler.
    /// <paramref name="offset"/> infinite-scroll icin: ilk N kayit atlanir (OFFSET ... FETCH).
    /// <paramref name="cardGroupIds"/> 5 elemanli dizi — her level icin secili grup id'si
    /// (0 = filter yok). Pozitif degerler card_group_mappings'e INNER JOIN ile filtre uygular.
    /// </summary>
    private async Task<List<RecipientRow>> LoadRecipientsAsync(
        int[] ids, string? search, int top, int offset, int[]? cardGroupIds, CancellationToken ct)
    {
        var list = new List<RecipientRow>();
        if (ids.Length == 0) return list;

        var companyId = ResolveCompanyId();
        var positiveIds = ids.Where(x => x > 0).ToArray();
        var includeCorporate = ids.Contains(VIRTUAL_CORPORATE_TITLE_ID);

        // LIKE pattern — sadece dolu arama metni icin set edilir
        var qTrim = (search ?? string.Empty).Trim();
        var qLike = qTrim.Length > 0 ? "%" + qTrim.Replace("%", "[%]").Replace("_", "[_]") + "%" : null;
        // top guard: TOP 0 / negatif olmasin; int.MaxValue zaten "limitsiz" anlami
        if (top <= 0) top = 1;
        if (offset < 0) offset = 0;

        // Pagination clause: OFFSET ... FETCH NEXT — sayfada gezme icin gerekli.
        // OFFSET=0 + FETCH NEXT N = ilk N (top ile esdeger).
        // int.MaxValue olunca "tum kayitlar" demek → OFFSET 0, FETCH yok yerine FETCH cok buyuk.
        // SQL Server FETCH bigint kabul ediyor, MaxValue ile sorun yok.
        var paginationClause = top == int.MaxValue && offset == 0
            ? string.Empty
            : $"OFFSET {offset} ROWS FETCH NEXT {top} ROWS ONLY";

        // Card group filtreleri — 5 level cascade. Her seviyede pozitif id varsa
        // card_group_mappings'e INNER JOIN eklenir (entity_type=2 = Contact).
        // c.[Id] INT → entity_id NVARCHAR(50) cast'i index'i bozar; veri kucuk oldugu
        // icin pratik etki yok ama gelecekte entity_id INT'e gocurmek dusunulebilir.
        var cardGroupJoin = new System.Text.StringBuilder();
        var cardGroupActive = new List<(int Level, int GroupId)>();
        if (cardGroupIds != null)
        {
            for (int level = 1; level <= Math.Min(5, cardGroupIds.Length); level++)
            {
                var gid = cardGroupIds[level - 1];
                if (gid > 0)
                {
                    var alias = "mg" + level;
                    cardGroupJoin.AppendLine($@"
                INNER JOIN [{_schema}].[card_group_mappings] {alias}
                        ON {alias}.[entity_id]     = CAST(c.[Id] AS NVARCHAR(50))
                       AND {alias}.[entity_type]   = 2
                       AND {alias}.[level]         = {level}
                       AND {alias}.[card_group_id] = @G{level}");
                    cardGroupActive.Add((level, gid));
                }
            }
        }
        var cardGroupJoinSql = cardGroupJoin.ToString();

        await using var conn = await _connFactory.OpenConnectionAsync(ct);

        // 1) Pozitif TitleId'ler icin ContactPerson tabanli alicilar
        if (positiveIds.Length > 0)
        {
            var paramList = string.Join(',', positiveIds.Select((_, i) => "@T" + i));
            var searchClause = qLike != null
                ? @" AND (cp.[FullName] LIKE @Q OR cp.[Email] LIKE @Q
                        OR c.[AccountTitle] LIKE @Q OR c.[AccountCode] LIKE @Q)"
                : string.Empty;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cp.[Id], cp.[FullName], ISNULL(t.[Name], N'') AS Title, cp.[Email], cp.[Phone],
                       c.[Id] AS ContactId, c.[AccountCode], c.[AccountTitle]
                FROM [{_schema}].[ContactPerson] cp
                INNER JOIN [{_schema}].[Contact] c ON c.[Id] = cp.[ContactId]
                LEFT  JOIN [{_schema}].[ContactPersonTitle] t ON t.[Id] = cp.[TitleId]
                {cardGroupJoinSql}
                WHERE cp.[IsActive] = 1
                  AND c.[IsActive] = 1
                  AND cp.[Email] IS NOT NULL AND cp.[Email] <> N''
                  AND cp.[TitleId] IN ({paramList})
                  AND (@CompanyId = 0 OR c.[CompanyId] = @CompanyId)
                  {searchClause}
                ORDER BY c.[AccountTitle], cp.[FullName]
                {paginationClause};";
            cmd.Parameters.Add(new SqlParameter("@CompanyId", System.Data.SqlDbType.Int) { Value = companyId });
            for (int i = 0; i < positiveIds.Length; i++)
                cmd.Parameters.Add(new SqlParameter("@T" + i, positiveIds[i]));
            if (qLike != null)
                cmd.Parameters.Add(new SqlParameter("@Q", System.Data.SqlDbType.NVarChar, 200) { Value = qLike });
            foreach (var (level, gid) in cardGroupActive)
                cmd.Parameters.Add(new SqlParameter("@G" + level, System.Data.SqlDbType.Int) { Value = gid });

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new RecipientRow(
                    r.GetInt32(0),
                    r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(3) ? "" : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetInt32(5),
                    r.IsDBNull(6) ? "" : r.GetString(6),
                    r.IsDBNull(7) ? "" : r.GetString(7)));
            }
        }

        // 2) Virtual "Kurumsal Iletisim" — Contact.Email (cari kurumsal mail).
        // ASIL BUYUK TABLO: binlerce/onbinlerce cari olabilir. TOP + sargable WHERE
        // + arama parametresi performansin omurgasi.
        if (includeCorporate)
        {
            var searchClause = qLike != null
                ? @" AND (c.[AccountTitle] LIKE @Q OR c.[AccountCode] LIKE @Q OR c.[Email] LIKE @Q)"
                : string.Empty;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT c.[Id] AS ContactId, c.[AccountCode], c.[AccountTitle], c.[Email], c.[Phone]
                FROM [{_schema}].[Contact] c
                {cardGroupJoinSql}
                WHERE c.[IsActive] = 1
                  AND c.[Email] IS NOT NULL AND c.[Email] <> N''
                  AND (@CompanyId = 0 OR c.[CompanyId] = @CompanyId)
                  {searchClause}
                ORDER BY c.[AccountTitle], c.[Id]
                {paginationClause};";
            cmd.Parameters.Add(new SqlParameter("@CompanyId", System.Data.SqlDbType.Int) { Value = companyId });
            if (qLike != null)
                cmd.Parameters.Add(new SqlParameter("@Q", System.Data.SqlDbType.NVarChar, 200) { Value = qLike });
            foreach (var (level, gid) in cardGroupActive)
                cmd.Parameters.Add(new SqlParameter("@G" + level, System.Data.SqlDbType.Int) { Value = gid });

            await using var r = await cmd.ExecuteReaderAsync(ct);
            // ContactPerson Id'leri ile cakismayan suni Id'ler: 1_000_000_000 + ContactId.
            while (await r.ReadAsync(ct))
            {
                var contactId    = r.GetInt32(0);
                var contactCode  = r.IsDBNull(1) ? "" : r.GetString(1);
                var contactTitle = r.IsDBNull(2) ? "" : r.GetString(2);
                var email        = r.IsDBNull(3) ? "" : r.GetString(3);
                var phone        = r.IsDBNull(4) ? null : r.GetString(4);
                list.Add(new RecipientRow(
                    Id:           1_000_000_000 + contactId,
                    FullName:     contactTitle,        // alicinin gozuktugu ad = cari unvan
                    Title:        "Kurumsal İletişim",
                    Email:        email,
                    Phone:        phone,
                    ContactId:    contactId,
                    ContactCode:  contactCode,
                    ContactTitle: contactTitle));
            }
        }

        return list;
    }

    private async Task<List<RecipientRow>> LoadRecipientsByPersonIdsAsync(int[] personIds, CancellationToken ct)
    {
        var list = new List<RecipientRow>();
        if (personIds.Length == 0) return list;

        var companyId = ResolveCompanyId();
        // Virtual "Kurumsal İletişim" alicilarinin Id'si 1_000_000_000 + ContactId.
        // ContactPerson Id'leri < 1B kabul ediliyor — 2 set'e ayir.
        const int CORPORATE_ID_OFFSET = 1_000_000_000;
        var normalPersonIds = personIds.Where(x => x > 0 && x < CORPORATE_ID_OFFSET).ToArray();
        var corporateContactIds = personIds
            .Where(x => x >= CORPORATE_ID_OFFSET)
            .Select(x => x - CORPORATE_ID_OFFSET)
            .ToArray();

        await using var conn = await _connFactory.OpenConnectionAsync(ct);

        // 1) Normal ContactPerson recipient'lar
        if (normalPersonIds.Length > 0)
        {
            var paramList = string.Join(',', normalPersonIds.Select((_, i) => "@P" + i));
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cp.[Id], cp.[FullName], ISNULL(t.[Name], N'') AS Title, cp.[Email], cp.[Phone],
                       c.[Id] AS ContactId, c.[AccountCode], c.[AccountTitle]
                FROM [{_schema}].[ContactPerson] cp
                INNER JOIN [{_schema}].[Contact] c ON c.[Id] = cp.[ContactId]
                LEFT  JOIN [{_schema}].[ContactPersonTitle] t ON t.[Id] = cp.[TitleId]
                WHERE cp.[IsActive] = 1
                  AND c.[IsActive] = 1
                  AND cp.[Email] IS NOT NULL AND LTRIM(RTRIM(cp.[Email])) <> N''
                  AND cp.[Id] IN ({paramList})
                  AND (@CompanyId = 0 OR c.[CompanyId] = @CompanyId)
                ORDER BY c.[AccountTitle], cp.[FullName];";
            cmd.Parameters.Add(new SqlParameter("@CompanyId", System.Data.SqlDbType.Int) { Value = companyId });
            for (int i = 0; i < normalPersonIds.Length; i++)
                cmd.Parameters.Add(new SqlParameter("@P" + i, normalPersonIds[i]));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new RecipientRow(
                    r.GetInt32(0),
                    r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(3) ? "" : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetInt32(5),
                    r.IsDBNull(6) ? "" : r.GetString(6),
                    r.IsDBNull(7) ? "" : r.GetString(7)));
            }
        }

        // 2) Kurumsal İletişim (Contact tabanli) recipient'lar
        if (corporateContactIds.Length > 0)
        {
            var paramList = string.Join(',', corporateContactIds.Select((_, i) => "@C" + i));
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT c.[Id], c.[AccountCode], c.[AccountTitle], c.[Email], c.[Phone]
                FROM [{_schema}].[Contact] c
                WHERE c.[IsActive] = 1
                  AND c.[Email] IS NOT NULL AND LTRIM(RTRIM(c.[Email])) <> N''
                  AND c.[Id] IN ({paramList})
                  AND (@CompanyId = 0 OR c.[CompanyId] = @CompanyId)
                ORDER BY c.[AccountTitle];";
            cmd.Parameters.Add(new SqlParameter("@CompanyId", System.Data.SqlDbType.Int) { Value = companyId });
            for (int i = 0; i < corporateContactIds.Length; i++)
                cmd.Parameters.Add(new SqlParameter("@C" + i, corporateContactIds[i]));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var contactId    = r.GetInt32(0);
                var contactCode  = r.IsDBNull(1) ? "" : r.GetString(1);
                var contactTitle = r.IsDBNull(2) ? "" : r.GetString(2);
                var email        = r.IsDBNull(3) ? "" : r.GetString(3);
                var phone        = r.IsDBNull(4) ? null : r.GetString(4);
                list.Add(new RecipientRow(
                    Id:           CORPORATE_ID_OFFSET + contactId,
                    FullName:     contactTitle,
                    Title:        "Kurumsal İletişim",
                    Email:        email,
                    Phone:        phone,
                    ContactId:    contactId,
                    ContactCode:  contactCode,
                    ContactTitle: contactTitle));
            }
        }

        return list;
    }

    private async Task<string[]> LoadTitleNamesAsync(int[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return Array.Empty<string>();
        var list = new List<string>();

        // Virtual "Kurumsal İletişim" varsa onu da listele
        if (ids.Contains(VIRTUAL_CORPORATE_TITLE_ID))
            list.Add("Kurumsal İletişim");

        var positiveIds = ids.Where(x => x > 0).ToArray();
        if (positiveIds.Length > 0)
        {
            var paramList = string.Join(',', positiveIds.Select((_, i) => "@T" + i));
            await using var conn = await _connFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT [Name] FROM [{_schema}].[ContactPersonTitle] WHERE [Id] IN ({paramList});";
            for (int i = 0; i < positiveIds.Length; i++)
                cmd.Parameters.Add(new SqlParameter("@T" + i, positiveIds[i]));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(r.IsDBNull(0) ? "" : r.GetString(0));
        }
        return list.ToArray();
    }

    private static string[] SafeJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private async Task<(DocLayoutDetailDto? layout, (int Id, string? Code, string? Title, string? Email)? contact, string? error)>
        LoadLayoutAndContactAsync(int layoutId, int contactId, CancellationToken ct)
    {
        var layout = await _docDesignerService.GetAsync(layoutId, ct);
        if (layout == null)
            return (null, null, "Sablon bulunamadi.");
        // 2026-05-20: UseAsMailTemplate bayragi VEYA legacy OutputFormat=email kabul.
        var isMailLayout = layout.UseAsMailTemplate
                        || string.Equals(layout.OutputFormat, "email", StringComparison.OrdinalIgnoreCase);
        if (!isMailLayout)
            return (null, null, "Bu dizayn mail sablonu olarak isaretlenmemis. Belge Tasarimcisi'nda 'Mail sablonu olarak da kullan' secenegini acin.");

        (int Id, string? Code, string? Title, string? Email)? contact = null;
        if (contactId > 0)
        {
            await using var conn = await _connFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT TOP 1 [Id],[AccountCode],[AccountTitle],[Email]
                FROM [{_schema}].[Contact] WHERE [Id] = @Id AND [IsActive] = 1;";
            cmd.Parameters.AddWithValue("@Id", contactId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                contact = (
                    r.GetInt32(0),
                    r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3));
            }
            else
            {
                return (layout, null, "Cari bulunamadi.");
            }
        }
        return (layout, contact, null);
    }

    private static Dictionary<string, string> BuildTokens((int Id, string? Code, string? Title, string? Email)? contact)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["currentDate"] = DateTime.Now.ToString("dd.MM.yyyy"),
            ["contactName"] = contact?.Title ?? string.Empty,
            ["contactEmail"] = contact?.Email ?? string.Empty,
            ["contactCode"] = contact?.Code ?? string.Empty,
        };
        return dict;
    }

    private int ResolveCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    /// <summary>Dosya adından path traversal / kontrol karakterleri temizler.</summary>
    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "ek";
        var bad = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !bad.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return "ek";
        // Path bilesenlerini at — sadece son segmenti al
        cleaned = Path.GetFileName(cleaned);
        return cleaned.Length > 200 ? cleaned.Substring(0, 200) : cleaned;
    }
}
