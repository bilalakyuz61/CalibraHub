using System.Text;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Belge bazli (Satis Teklifi, Siparis, Fatura, Irsaliye...) ortak mail
/// gonderim ekrani. Her belge turu icin ayri bir mail ekrani acmak yerine
/// bu controller tek arayuzle butun belgelere hizmet eder.
///
/// Kullanim:
///   SmartCard extraAction:
///     { type:'fetch-modal', fetchUrl:'/Document/MailDialog?id={id}&type=QUOTE',
///       modalTitle:'Mail Gonder' }
///
/// Sablon turleri (type parametresi):
///   QUOTE    → Satis Teklifi
///   ORDER    → Satis Siparisi
///   INVOICE  → Satis Faturasi
///   DISPATCH → Irsaliye
///   (Ileride eklenenler icin PrepareContext switch'ine case ekleyin.)
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.BulkMail)]
public sealed class DocumentMailController : Controller
{
    private readonly IDocumentService _quoteService;

    public DocumentMailController(IDocumentService quoteService)
    {
        _quoteService = quoteService;
    }

    // Her belge turu icin gerekli context — baslik, default konu/gorsel metin
    private sealed record MailContext(
        string DocumentNumber,
        string ContactName,
        string DocumentTypeLabel, // "Satis Teklifi", "Siparis", vb.
        string? ContactEmail);

    private async Task<MailContext?> PrepareContextAsync(int id, string type, CancellationToken ct)
    {
        type = (type ?? string.Empty).Trim().ToUpperInvariant();
        return type switch
        {
            "QUOTE" or "" => await LoadQuoteContextAsync(id, "Satis Teklifi", ct),
            "ORDER"       => await LoadQuoteContextAsync(id, "Satis Siparisi", ct),
            "INVOICE"     => await LoadQuoteContextAsync(id, "Satis Faturasi", ct),
            "DISPATCH"    => await LoadQuoteContextAsync(id, "Irsaliye", ct),
            _             => await LoadQuoteContextAsync(id, "Belge", ct),
        };
    }

    // Tum belge turleri su an DocumentService'ten cekiliyor — ileride ayri
    // service'ler (InvoiceService, DispatchService) oldugunda burada dallanirsin.
    private async Task<MailContext?> LoadQuoteContextAsync(int id, string typeLabel, CancellationToken ct)
    {
        var doc = await _quoteService.GetQuoteByIdAsync(id, ct);
        if (doc == null) return null;
        return new MailContext(
            DocumentNumber: doc.DocumentNumber ?? string.Empty,
            ContactName:    doc.ContactName    ?? "-",
            DocumentTypeLabel: typeLabel,
            ContactEmail:   null /* TODO: IContactRepository.GetEmail(doc.ContactId) */);
    }

    /// <summary>
    /// Modal icinde acilacak mail formu HTML'i. SmartCard.fetch-modal
    /// dangerouslySetInnerHTML ile basar; bu yuzden HTML self-contained,
    /// <style> + <script> icerir.
    /// </summary>
    [HttpGet("/Document/MailDialog")]
    public async Task<IActionResult> MailDialog(int id, string? type, CancellationToken ct)
    {
        var ctx = await PrepareContextAsync(id, type ?? string.Empty, ct);
        if (ctx == null) return NotFound();

        var docNo   = System.Net.WebUtility.HtmlEncode(ctx.DocumentNumber);
        var contact = System.Net.WebUtility.HtmlEncode(ctx.ContactName);
        var typeLbl = System.Net.WebUtility.HtmlEncode(ctx.DocumentTypeLabel);
        var email   = System.Net.WebUtility.HtmlEncode(ctx.ContactEmail ?? string.Empty);
        var docType = System.Net.WebUtility.HtmlEncode((type ?? "QUOTE").ToUpperInvariant());

        var sb = new StringBuilder();
        sb.Append("""
<style>
  /* ── Mail dialog DINAMIK tema ──
     Form root'a JS ile sq-mail-theme-dark / sq-mail-theme-light class'i eklenir.
     App temasi degisirse MutationObserver yakalar ve class'i guncelle. Modal
     wrapper (SmartCard'in bg-white dark:bg-slate-900) Tailwind tema default'u
     ile giderken sadece DARK tema tespit edildiginde force-dark override'i
     devreye girer — LIGHT modda beyaz kart dogal bir sekilde gosterilir. */

  /* ── DARK tema aktifken: modal karti koyu temaya cevir ── */
  div[class*="rounded-2xl"]:has(.sq-mail-form.sq-mail-theme-dark) {
      background: #0f172a !important;
      color: rgba(255,255,255,.92) !important;
  }
  div[class*="rounded-2xl"]:has(.sq-mail-form.sq-mail-theme-dark) > div[class*="border-b"] {
      border-bottom-color: rgba(255,255,255,.08) !important;
      background: transparent !important;
  }
  div[class*="rounded-2xl"]:has(.sq-mail-form.sq-mail-theme-dark) h3 {
      color: rgba(255,255,255,.95) !important;
  }
  div[class*="rounded-2xl"]:has(.sq-mail-form.sq-mail-theme-dark) > div[class*="border-b"] button:hover {
      background: rgba(255,255,255,.06) !important;
  }
  /* LIGHT tema aktifken: herhangi bir override YOK — Tailwind default (bg-white) gecerli */

  .sq-mail-form { padding:4px 2px 8px; display:flex; flex-direction:column; gap:14px;
                  font-family:inherit; box-sizing:border-box;
                  color:rgba(255,255,255,.92); }
  .sq-mail-header { display:flex; align-items:center; gap:12px; padding-bottom:12px;
                    border-bottom:1px solid rgba(148,163,184,.22); margin-bottom:4px; }
  .sq-mail-header__icon { width:40px; height:40px; border-radius:12px;
                          display:flex; align-items:center; justify-content:center;
                          background:linear-gradient(135deg, rgba(99,102,241,.22), rgba(168,85,247,.18));
                          border:1px solid rgba(99,102,241,.35); color:#a5b4fc; flex-shrink:0; }
  .sq-mail-header__text { flex:1; min-width:0; }
  .sq-mail-header__title { font-size:14px; font-weight:700; letter-spacing:-.01em; margin:0; }
  .sq-mail-header__sub   { font-size:11.5px; opacity:.62; margin-top:2px; }

  .sq-mail-field { display:flex; flex-direction:column; gap:5px; }
  .sq-mail-field__label { font-size:10.5px; font-weight:700;
                          letter-spacing:.04em; text-transform:uppercase; opacity:.62; }
  .sq-mail-input,
  .sq-mail-textarea {
      font:inherit; font-size:13px; padding:9px 11px; border-radius:9px;
      outline:none; width:100%; box-sizing:border-box; resize:none;
      border:1px solid rgba(148,163,184,.28); background:transparent;
      transition:border-color .12s, box-shadow .12s, background .12s;
  }
  .sq-mail-textarea { resize:vertical; min-height:130px; font-family:inherit; line-height:1.5; }
  .sq-mail-input:focus,
  .sq-mail-textarea:focus {
      border-color:rgba(99,102,241,.65);
      box-shadow:0 0 0 3px rgba(99,102,241,.18);
  }

  .sq-mail-actions { display:flex; justify-content:flex-end; gap:8px; margin-top:6px; }
  .sq-mail-btn { font:inherit; font-size:12.5px; font-weight:700; padding:9px 20px;
                 border-radius:9px; cursor:pointer; letter-spacing:.01em;
                 transition:transform .1s, filter .12s, box-shadow .12s;
                 border:1px solid transparent; }
  .sq-mail-btn:hover:not(:disabled) { transform:translateY(-1px); filter:brightness(1.08); }
  .sq-mail-btn:disabled { opacity:.6; cursor:not-allowed; }
  .sq-mail-btn--primary { background:linear-gradient(135deg,#6366f1,#4f46e5);
                          color:#fff !important; box-shadow:0 4px 14px rgba(99,102,241,.28); }
  .sq-mail-btn--ghost   { background:transparent; }

  .sq-mail-success { padding:32px 16px; text-align:center; font-size:13.5px; font-weight:600;
                     display:flex; flex-direction:column; align-items:center; gap:10px; }
  .sq-mail-success__icon { width:44px; height:44px; border-radius:50%;
                           display:flex; align-items:center; justify-content:center;
                           font-size:22px; font-weight:800;
                           background:rgba(34,197,94,.16); color:#22c55e;
                           border:1px solid rgba(34,197,94,.35); }

  /* ── KOYU TEMA (varsayilan, TUM modlar icin) ── */
  .sq-mail-form .sq-mail-input,
  .sq-mail-form .sq-mail-textarea {
      background:rgba(10,14,24,.55) !important;
      color:rgba(255,255,255,.95) !important;
      border-color:rgba(255,255,255,.14) !important;
  }
  .sq-mail-form .sq-mail-input::placeholder,
  .sq-mail-form .sq-mail-textarea::placeholder { color:rgba(255,255,255,.28) !important; }
  .sq-mail-form .sq-mail-btn--ghost {
      color:rgba(255,255,255,.82) !important;
      border-color:rgba(255,255,255,.14) !important;
      background:rgba(255,255,255,.03) !important;
  }
  .sq-mail-form .sq-mail-btn--ghost:hover {
      background:rgba(255,255,255,.09) !important; color:#fff !important;
  }
  .sq-mail-form .sq-mail-field__label { opacity:.48; color:rgba(255,255,255,.78); }
  .sq-mail-form .sq-mail-header { border-bottom-color:rgba(255,255,255,.10); }
  .sq-mail-form .sq-mail-header__title { color:rgba(255,255,255,.95); }

  /* Chrome autofill — sari zemin yerine koyu kalsin */
  .sq-mail-form .sq-mail-input:-webkit-autofill,
  .sq-mail-form .sq-mail-textarea:-webkit-autofill {
      box-shadow: 0 0 0 1000px rgba(10,14,24,.95) inset !important;
      -webkit-text-fill-color: rgba(255,255,255,.95) !important;
      caret-color: rgba(255,255,255,.95);
      transition: background-color 5000s ease-in-out 0s;
  }
</style>
""");

        sb.Append("<form class=\"sq-mail-form\" data-doc-id=\"").Append(id)
          .Append("\" data-doc-type=\"").Append(docType)
          .Append("\" onsubmit=\"return window.sqSendDocMail(event)\" novalidate>\n");

        sb.Append("""
  <div class="sq-mail-header">
    <div class="sq-mail-header__icon" aria-hidden="true">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
        <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
        <polyline points="22,6 12,13 2,6"/>
      </svg>
    </div>
    <div class="sq-mail-header__text">
""");
        sb.Append("      <div class=\"sq-mail-header__title\">").Append(typeLbl).Append(" ").Append(docNo).Append("</div>\n");
        sb.Append("      <div class=\"sq-mail-header__sub\">Alici: ").Append(contact).Append("</div>\n");
        sb.Append("""
    </div>
  </div>
""");

        sb.Append("""
  <div class="sq-mail-field">
    <label class="sq-mail-field__label" for="sqMailTo">Alici E-posta</label>
""");
        sb.Append("    <input id=\"sqMailTo\" class=\"sq-mail-input\" name=\"to\" type=\"email\" required placeholder=\"ornek@firma.com\" value=\"").Append(email).Append("\" />\n");
        sb.Append("""
  </div>
  <div class="sq-mail-field">
    <label class="sq-mail-field__label" for="sqMailSubject">Konu</label>
""");
        sb.Append("    <input id=\"sqMailSubject\" class=\"sq-mail-input\" name=\"subject\" type=\"text\" required value=\"")
          .Append(typeLbl).Append(" ").Append(docNo).Append("\" />\n");
        sb.Append("""
  </div>
  <div class="sq-mail-field">
    <label class="sq-mail-field__label" for="sqMailBody">Mesaj</label>
""");
        sb.Append("    <textarea id=\"sqMailBody\" class=\"sq-mail-textarea\" name=\"body\" rows=\"7\">Sayin ").Append(contact)
          .Append(",&#10;&#10;Ekteki ").Append(docNo).Append(" numarali ").Append(typeLbl.ToLowerInvariant())
          .Append(" belgemizi degerlendirmenize sunariz.&#10;&#10;Saygilarimizla</textarea>\n");
        sb.Append("""
  </div>
  <div class="sq-mail-actions">
    <button type="button" class="sq-mail-btn sq-mail-btn--ghost" onclick="window.sqCloseDocMail(this)">Iptal</button>
    <button type="submit" class="sq-mail-btn sq-mail-btn--primary">Gonder</button>
  </div>
</form>
<script>
(function () {
    // Mail dialog uygulamanin birincil temasi (koyu) icin optimize edildi.
    // SmartCard modal wrapper Tailwind'in bg-white dark:bg-slate-900 kullanir;
    // iframe/parent tema sync'i her ortamda guvenilir olmadigi icin DOGRUDAN
    // modal wrapper DOM elementini bulup koyu zemin class'i (sq-mail-force-dark-modal)
    // ekliyoruz. Bu sayede Tailwind'in tema class'i ne olursa olsun mail modal
    // her zaman koyu cizilir.
    function forceDarkModalWrapper() {
        var form = document.querySelector('.sq-mail-form');
        if (!form) return;
        // Form'dan yukari tirman — SmartCard modal karti (bg-white / dark:bg-slate-900
        // class'ina sahip en yakin parent). En fazla 8 seviye.
        var el = form.parentElement;
        var hops = 0;
        while (el && el !== document.body && hops < 8) {
            var cls = el.className || '';
            // Tailwind bg-white veya rounded-2xl modal karti
            if (typeof cls === 'string' &&
                (cls.indexOf('rounded-2xl') >= 0 || cls.indexOf('bg-white') >= 0 ||
                 cls.indexOf('bg-slate-900') >= 0)) {
                el.classList.add('sq-mail-force-dark-modal');
                break;
            }
            el = el.parentElement;
            hops++;
        }
    }
    forceDarkModalWrapper();

    window.sqCloseDocMail = function (btn) {
        // SmartCard modal'inin kapat butonunu bul ve tetikle
        var root = btn;
        while (root && root !== document.body) {
            var candidates = root.querySelectorAll('button');
            for (var i = 0; i < candidates.length; i++) {
                var b = candidates[i];
                if (b.querySelector('svg') && b !== btn) {
                    if (b.getAttribute('aria-label') === 'Kapat' ||
                        b.getAttribute('aria-label') === 'Close' ||
                        b.classList.contains('sm-modal-close') ||
                        b.hasAttribute('data-dialog-close')) {
                        b.click(); return;
                    }
                }
            }
            root = root.parentElement;
        }
        // Fallback: SmartCard modal backdrop click
        var backdrops = document.querySelectorAll('div[class*="fixed"][class*="inset-0"]');
        if (backdrops.length > 0) { backdrops[backdrops.length - 1].click(); return; }
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    };
    window.sqSendDocMail = function (e) {
        e.preventDefault();
        var f = e.target;
        var submitBtn = f.querySelector('button[type="submit"]');
        if (submitBtn) { submitBtn.disabled = true; submitBtn.textContent = 'Gonderiliyor…'; }
        var docId = f.getAttribute('data-doc-id');
        var docType = f.getAttribute('data-doc-type');
        var fd = new FormData(f);
        fd.append('documentId', docId);
        fd.append('documentType', docType);
        fetch('/Document/SendMail', { method: 'POST', body: fd, credentials: 'same-origin' })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                if (d && d.success) {
                    f.outerHTML =
                        '<div class="sq-mail-form">'
                        + '<div class="sq-mail-success"><div class="sq-mail-success__icon">&#10003;</div>Mail gonderildi.</div>'
                        + '</div>';
                } else {
                    if (submitBtn) { submitBtn.disabled = false; submitBtn.textContent = 'Gonder'; }
                    var m = 'Gonderilemedi: ' + (d && d.message ? d.message : 'Bilinmeyen hata');
                    if (window.CalibraAlert && CalibraAlert.error) CalibraAlert.error(m);
                    else if (window.CalibraHub && CalibraHub.toast) CalibraHub.toast(m, 'err');
                    else alert(m);
                }
            })
            .catch(function (err) {
                if (submitBtn) { submitBtn.disabled = false; submitBtn.textContent = 'Gonder'; }
                var em = 'Hata: ' + err.message;
                if (window.CalibraAlert && CalibraAlert.error) CalibraAlert.error(em);
                else if (window.CalibraHub && CalibraHub.toast) CalibraHub.toast(em, 'err');
                else alert(em);
            });
        return false;
    };
})();
</script>
""");
        return Content(sb.ToString(), "text/html; charset=utf-8");
    }

    /// <summary>
    /// Belge bazli mail gonderme endpoint'i. SMTP yapilandirmasi eklendiginde
    /// IEmailSender entegrasyonu yapilir; su an simulasyon — kuyruga aliyormus
    /// gibi basarili cevap doner, console'a log yazar.
    /// </summary>
    [HttpPost("/Document/SendMail")]
    public IActionResult SendMail(int documentId, string documentType, string to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(to))
            return Json(new { success = false, message = "Alici boş." });

        // TODO: IEmailSender / SMTP entegrasyonu buraya gelir.
        //       Su an basarili cevap + console log ile UI akisini test edersiniz.
        Console.WriteLine($"[DocumentMail] type={documentType} id={documentId} to={to} subject={subject}");
        return Json(new { success = true, message = "Mail kuyruga alindi (simulasyon)." });
    }
}
