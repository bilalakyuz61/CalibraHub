/**
 * CalibraAlert — ekran ortasinda modal-style uyari/hata gosterimi.
 * Native browser alert() yerine tema-uyumlu (light/dark) overlay.
 *
 * Kullanim:
 *   CalibraAlert.show('Mesaj metni');
 *   CalibraAlert.show('Mesaj', { type: 'error' | 'warning' | 'success' | 'info' });
 *   CalibraAlert.show('Kaydedildi', { type: 'success', autoDismissMs: 2500 });
 *
 * Geriye Promise doner (OK / auto-dismiss / backdrop tiklama).
 */
(function () {
    'use strict';

    var TYPE_CONFIG = {
        error:   { accent: '#ef4444', bg: 'rgba(239,68,68,0.12)',  icon: '⚠',  title: 'Hata' },
        warning: { accent: '#f59e0b', bg: 'rgba(245,158,11,0.12)', icon: '⚠',  title: 'Uyari' },
        success: { accent: '#10b981', bg: 'rgba(16,185,129,0.12)', icon: '✓',  title: 'Basarili' },
        info:    { accent: '#3b82f6', bg: 'rgba(59,130,246,0.12)', icon: 'ℹ',  title: 'Bilgi' },
    };

    function isDark() {
        return document.documentElement.classList.contains('dark')
            || document.body.classList.contains('app-theme-dark');
    }

    function show(message, options) {
        var opts = options || {};
        var type = opts.type || 'info';
        var config = TYPE_CONFIG[type] || TYPE_CONFIG.info;
        var title = opts.title || config.title;
        var autoDismissMs = opts.autoDismissMs || 0;

        return new Promise(function (resolve) {
            // Onceki alert'i temizle
            var old = document.getElementById('__calibra_alert__');
            if (old) old.remove();

            var dark = isDark();
            var surface = dark ? 'rgba(18,24,38,0.98)' : '#ffffff';
            var textColor = dark ? '#e2e8f0' : '#1e293b';
            var borderColor = dark ? 'rgba(255,255,255,0.12)' : '#e2e8f0';
            var backdropBg = dark ? 'rgba(0,0,0,0.55)' : 'rgba(15,23,42,0.35)';

            var overlay = document.createElement('div');
            overlay.id = '__calibra_alert__';
            overlay.setAttribute('role', 'alertdialog');
            overlay.setAttribute('aria-modal', 'true');
            overlay.style.cssText = [
                'position:fixed', 'top:0', 'left:0', 'right:0', 'bottom:0',
                'z-index:9999',
                'display:flex', 'align-items:center', 'justify-content:center',
                'background:' + backdropBg,
                'backdrop-filter:blur(2px)',
                '-webkit-backdrop-filter:blur(2px)',
                'animation:__ca_fadein__ 120ms ease-out',
            ].join(';');

            var box = document.createElement('div');
            box.style.cssText = [
                'min-width:320px', 'max-width:520px',
                'background:' + surface,
                'color:' + textColor,
                'border:1px solid ' + borderColor,
                'border-left:4px solid ' + config.accent,
                'border-radius:10px',
                'box-shadow:0 20px 50px rgba(0,0,0,0.35)',
                'padding:18px 20px',
                'font-family:inherit', 'font-size:0.9rem', 'line-height:1.5',
                'animation:__ca_popin__ 160ms cubic-bezier(0.2,0.8,0.3,1)',
            ].join(';');

            var head = document.createElement('div');
            head.style.cssText = 'display:flex;align-items:center;gap:10px;margin-bottom:10px;';
            head.innerHTML =
                '<div style="width:32px;height:32px;border-radius:50%;display:flex;align-items:center;' +
                'justify-content:center;background:' + config.bg + ';color:' + config.accent +
                ';font-size:18px;font-weight:bold;flex-shrink:0">' + config.icon + '</div>' +
                '<div style="font-weight:600;font-size:0.95rem">' + escapeHtml(title) + '</div>';
            box.appendChild(head);

            var body = document.createElement('div');
            body.style.cssText = 'margin-bottom:16px;white-space:pre-wrap;word-break:break-word;';
            body.textContent = message;
            box.appendChild(body);

            if (!autoDismissMs) {
                var actions = document.createElement('div');
                actions.style.cssText = 'display:flex;justify-content:flex-end;';
                var okBtn = document.createElement('button');
                okBtn.type = 'button';
                okBtn.textContent = opts.okText || 'Tamam';
                okBtn.style.cssText = [
                    'padding:7px 18px',
                    'background:' + config.accent, 'color:#ffffff',
                    'border:none', 'border-radius:6px',
                    'font-size:0.85rem', 'font-weight:600', 'cursor:pointer',
                    'transition:opacity 0.12s',
                ].join(';');
                okBtn.addEventListener('mouseenter', function () { okBtn.style.opacity = '0.88'; });
                okBtn.addEventListener('mouseleave', function () { okBtn.style.opacity = '1'; });
                okBtn.addEventListener('click', function () { close(); });
                actions.appendChild(okBtn);
                box.appendChild(actions);
                setTimeout(function () { okBtn.focus(); }, 50);
            }

            overlay.appendChild(box);

            ensureAnimations();
            document.body.appendChild(overlay);

            function close() {
                if (!overlay.parentNode) return;
                overlay.style.opacity = '0';
                overlay.style.transition = 'opacity 120ms';
                setTimeout(function () {
                    if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
                    resolve();
                }, 120);
            }

            overlay.addEventListener('click', function (e) {
                if (e.target === overlay) close();
            });
            document.addEventListener('keydown', onKey);
            function onKey(e) {
                if (e.key === 'Escape' || e.key === 'Enter') {
                    document.removeEventListener('keydown', onKey);
                    close();
                }
            }

            if (autoDismissMs > 0) {
                setTimeout(close, autoDismissMs);
            }
        });
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (ch) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch];
        });
    }

    function ensureAnimations(hostDoc) {
        var doc = hostDoc || document;
        if (doc.getElementById('__calibra_alert_keyframes__')) return;
        var style = doc.createElement('style');
        style.id = '__calibra_alert_keyframes__';
        style.textContent =
            '@keyframes __ca_fadein__ { from { opacity: 0; } to { opacity: 1; } }\n' +
            '@keyframes __ca_popin__ { from { opacity: 0; transform: translateY(-6px) scale(0.96); } to { opacity: 1; transform: translateY(0) scale(1); } }';
        doc.head.appendChild(style);
    }

    // Kisayol alias'lar
    function error(msg, opts)   { return show(msg, Object.assign({ type: 'error' },   opts || {})); }
    function warning(msg, opts) { return show(msg, Object.assign({ type: 'warning' }, opts || {})); }
    function success(msg, opts) { return show(msg, Object.assign({ type: 'success' }, opts || {})); }
    function info(msg, opts)    { return show(msg, Object.assign({ type: 'info' },    opts || {})); }

    /**
     * Onay modali — native confirm() yerine tema-uyumlu, ekran ortasinda render.
     * Promise<boolean> doner: Evet => true, İptal/Esc/backdrop => false.
     *
     * Iframe icinde (workspace frame) cagrilirsa modali parent window'a portal eder;
     * boylece modal iframe viewport'una degil, tam ekran ortasina oturur.
     */
    function confirmDialog(message, options) {
        var opts = options || {};
        var type = opts.type || 'warning';
        var config = TYPE_CONFIG[type] || TYPE_CONFIG.warning;
        var title = opts.title || 'Emin misiniz?';
        var okText = opts.okText || 'Evet';
        var cancelText = opts.cancelText || 'Iptal';
        var danger = opts.danger === true;
        if (danger) { config = TYPE_CONFIG.error; }

        // Tam ekran ortasinda render icin top-window body'ye mount et
        var hostDoc = document;
        try {
            if (window.top && window.top.document && window.top.document.body) {
                hostDoc = window.top.document;
            }
        } catch (e) { /* cross-origin — iframe'de kal */ }

        return new Promise(function (resolve) {
            var old = hostDoc.getElementById('__calibra_confirm__');
            if (old) old.remove();

            var dark = isDark();
            var surface    = dark ? 'rgba(18,24,38,0.98)' : '#ffffff';
            var textColor  = dark ? '#e2e8f0' : '#1e293b';
            var mutedColor = dark ? '#94a3b8' : '#64748b';
            var borderColor = dark ? 'rgba(255,255,255,0.12)' : '#e2e8f0';
            var cancelBg    = dark ? 'rgba(255,255,255,0.07)' : '#f1f5f9';
            var backdropBg  = dark ? 'rgba(0,0,0,0.6)' : 'rgba(15,23,42,0.4)';

            var overlay = hostDoc.createElement('div');
            overlay.id = '__calibra_confirm__';
            overlay.setAttribute('role', 'alertdialog');
            overlay.setAttribute('aria-modal', 'true');
            overlay.style.cssText = [
                'position:fixed', 'top:0', 'left:0', 'right:0', 'bottom:0',
                'z-index:100000',
                'display:flex', 'align-items:center', 'justify-content:center',
                'background:' + backdropBg,
                'backdrop-filter:blur(4px)',
                '-webkit-backdrop-filter:blur(4px)',
                'animation:__ca_fadein__ 120ms ease-out',
                'padding:20px'
            ].join(';');

            var box = hostDoc.createElement('div');
            box.style.cssText = [
                'min-width:320px', 'max-width:420px', 'width:90vw',
                'background:' + surface,
                'color:' + textColor,
                'border:1px solid ' + borderColor,
                'border-radius:16px',
                'box-shadow:0 24px 64px rgba(0,0,0,0.5)',
                'padding:28px 24px',
                'font-family:inherit', 'font-size:0.9rem', 'line-height:1.5',
                'display:flex', 'flex-direction:column', 'align-items:center',
                'gap:12px', 'text-align:center',
                'animation:__ca_popin__ 160ms cubic-bezier(0.2,0.8,0.3,1)'
            ].join(';');

            var iconWrap = hostDoc.createElement('div');
            iconWrap.style.cssText = [
                'width:48px', 'height:48px', 'border-radius:50%',
                'display:flex', 'align-items:center', 'justify-content:center',
                'background:' + config.bg, 'color:' + config.accent,
                'font-size:24px', 'font-weight:bold'
            ].join(';');
            iconWrap.textContent = config.icon;
            box.appendChild(iconWrap);

            var titleEl = hostDoc.createElement('h3');
            titleEl.style.cssText = 'font-size:1.05rem;font-weight:700;margin:0;color:' + textColor;
            titleEl.textContent = title;
            box.appendChild(titleEl);

            var bodyEl = hostDoc.createElement('p');
            bodyEl.style.cssText = 'font-size:.86rem;margin:0;color:' + mutedColor + ';white-space:pre-wrap;';
            bodyEl.textContent = message;
            box.appendChild(bodyEl);

            var actions = hostDoc.createElement('div');
            actions.style.cssText = 'display:flex;gap:10px;margin-top:8px;';

            var cancelBtn = hostDoc.createElement('button');
            cancelBtn.type = 'button';
            cancelBtn.textContent = cancelText;
            cancelBtn.style.cssText = [
                'padding:9px 18px', 'border-radius:8px',
                'font-size:.86rem', 'font-weight:600',
                'background:' + cancelBg, 'color:' + textColor,
                'border:1px solid ' + borderColor, 'cursor:pointer'
            ].join(';');

            var okBtn = hostDoc.createElement('button');
            okBtn.type = 'button';
            okBtn.textContent = okText;
            var okBg = danger
                ? 'linear-gradient(135deg,#ef4444,#dc2626)'
                : config.accent;
            okBtn.style.cssText = [
                'padding:9px 18px', 'border-radius:8px',
                'font-size:.86rem', 'font-weight:600',
                'background:' + okBg, 'color:#fff',
                'border:none', 'cursor:pointer'
            ].join(';');

            actions.appendChild(cancelBtn);
            actions.appendChild(okBtn);
            box.appendChild(actions);
            overlay.appendChild(box);

            ensureAnimations(hostDoc);
            hostDoc.body.appendChild(overlay);
            setTimeout(function () { okBtn.focus(); }, 50);

            function close(result) {
                if (!overlay.parentNode) return;
                hostDoc.removeEventListener('keydown', onKey);
                overlay.style.opacity = '0';
                overlay.style.transition = 'opacity 120ms';
                setTimeout(function () {
                    if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
                    resolve(result === true);
                }, 120);
            }
            cancelBtn.addEventListener('click', function () { close(false); });
            okBtn.addEventListener('click', function () { close(true); });
            overlay.addEventListener('click', function (e) { if (e.target === overlay) close(false); });
            function onKey(e) {
                if (e.key === 'Escape') close(false);
                else if (e.key === 'Enter') close(true);
            }
            hostDoc.addEventListener('keydown', onKey);
        });
    }

    window.CalibraAlert = { show: show, error: error, warning: warning, success: success, info: info, confirm: confirmDialog };
})();
