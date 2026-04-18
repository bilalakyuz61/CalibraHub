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

    function ensureAnimations() {
        if (document.getElementById('__calibra_alert_keyframes__')) return;
        var style = document.createElement('style');
        style.id = '__calibra_alert_keyframes__';
        style.textContent =
            '@keyframes __ca_fadein__ { from { opacity: 0; } to { opacity: 1; } }\n' +
            '@keyframes __ca_popin__ { from { opacity: 0; transform: translateY(-6px) scale(0.96); } to { opacity: 1; transform: translateY(0) scale(1); } }';
        document.head.appendChild(style);
    }

    // Kisayol alias'lar
    function error(msg, opts)   { return show(msg, Object.assign({ type: 'error' },   opts || {})); }
    function warning(msg, opts) { return show(msg, Object.assign({ type: 'warning' }, opts || {})); }
    function success(msg, opts) { return show(msg, Object.assign({ type: 'success' }, opts || {})); }
    function info(msg, opts)    { return show(msg, Object.assign({ type: 'info' },    opts || {})); }

    window.CalibraAlert = { show: show, error: error, warning: warning, success: success, info: info };
})();
