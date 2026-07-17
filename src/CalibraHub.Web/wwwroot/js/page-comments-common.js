/**
 * page-comments-common.js — Sayfa-içi Yorum sistemi ortak yardımcılar.
 *
 * Hem floating widget (page-comments-widget.js, her sayfada global) hem
 * yönetim ekranı (page-comments-admin.js, /PageComments) tarafından kullanılır.
 * API sözleşmesi KİLİTLİ — bkz. pc-backend paralel oturum:
 *   GET  /api/page-comments                         → { "<screenKey>": { comments:[...] } }
 *   POST /api/page-comments/comment                  ← {key,id,text,ts,author,pageTitle?,formCode?}
 *   POST /api/page-comments/comment/status            ← {key,id,status,version?,note?,user?}
 *   POST /api/page-comments/comment/edit               ← {key,id,text,user}
 *   POST /api/page-comments/comment/delete            ← {key,id}
 *   POST /api/page-comments/comment/image             ← {commentId,id,mime,dataBase64,name?}
 *   GET  /api/page-comments/comment/image/{id}        → binary
 *   POST /api/page-comments/comment/image/delete       ← {id}
 *   GET  /api/page-comments/activity
 *
 * window.CalibraPageComments namespace'i altında export edilir.
 */
(function () {
    'use strict';

    if (window.CalibraPageComments) return; // duplicate script tag guard

    var API_BASE = '/api/page-comments';

    var STATUS_META = {
        approved:  { label: 'Onaylandı',  cls: 'pc-badge--approved' },
        applied:   { label: 'Uygulandı',  cls: 'pc-badge--applied' },
        abandoned: { label: 'Vazgeçildi', cls: 'pc-badge--abandoned' },
        revised:   { label: 'Revize Edildi', cls: 'pc-badge--revised' }
    };

    function titleCaseWord(s) {
        return String(s || '').replace(/[_-]+/g, ' ').replace(/\s+/g, ' ').trim()
            .replace(/\S+/g, function (w) { return w.charAt(0).toUpperCase() + w.slice(1).toLowerCase(); });
    }

    /** Bilinmeyen/başlangıç durumu için de güvenli — backend'in ilk durum adı
     *  ne olursa olsun (open/new/pending vb.) nötr "slate" rozet + Title Case metin döner. */
    function statusMeta(status) {
        var key = String(status || '').trim().toLowerCase();
        if (STATUS_META[key]) return STATUS_META[key];
        return { label: status ? titleCaseWord(status) : 'Açık', cls: '' };
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /* Ekran anahtarı normalize: query/hash hariç, sondaki '/' kırpılır, lowercase. */
    function normalizeKey(pathname) {
        var p = String(pathname || '/').split('?')[0].split('#')[0];
        if (p.length > 1 && p.charAt(p.length - 1) === '/') p = p.slice(0, -1);
        p = p.toLowerCase();
        return p || '/';
    }

    function fmtDateTime(ts) {
        if (!ts) return '';
        var d = new Date(ts);
        if (isNaN(d.getTime())) return String(ts);
        function pad(n) { return n < 10 ? '0' + n : '' + n; }
        return pad(d.getDate()) + '.' + pad(d.getMonth() + 1) + '.' + d.getFullYear() +
            ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function uuid() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }
        // Eski tarayıcı fallback (RFC4122 benzeri, kriptografik değil — sadece istemci id'si).
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    function toast(type, message) {
        try {
            if (window.toast && typeof window.toast[type] === 'function') {
                window.toast[type](message, { position: 'top-right', duration: 4000 });
                return;
            }
            if (typeof window.showToast === 'function') {
                window.showToast({ type: type, message: message, position: 'top-right', duration: 4000 });
            }
        } catch (e) { /* sessiz gec */ }
    }

    function getCsrfToken(rootEl) {
        if (rootEl && rootEl.dataset && rootEl.dataset.csrf) return rootEl.dataset.csrf;
        var meta = document.querySelector('meta[name="csrf-token"]');
        if (meta && meta.content) return meta.content;
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        if (input && input.value) return input.value;
        return '';
    }

    function apiFetch(path, options, csrfToken) {
        options = options || {};
        var headers = Object.assign({}, options.headers || {});
        var method = (options.method || 'GET').toUpperCase();
        if (method !== 'GET' && csrfToken) {
            headers['RequestVerificationToken'] = csrfToken;
        }
        if (options.body && !headers['Content-Type']) {
            headers['Content-Type'] = 'application/json';
        }
        return fetch(API_BASE + path, Object.assign({ credentials: 'same-origin' }, options, { headers: headers }))
            .then(function (res) {
                if (!res.ok) {
                    return res.text().then(function (t) {
                        throw new Error('HTTP ' + res.status + (t ? (': ' + t.slice(0, 200)) : ''));
                    }, function () { throw new Error('HTTP ' + res.status); });
                }
                var ct = res.headers.get('content-type') || '';
                if (ct.indexOf('application/json') === -1) return null;
                return res.text().then(function (t) {
                    if (!t) return null;
                    try { return JSON.parse(t); } catch (e) { return null; }
                });
            });
    }

    function fileToBase64(blob) {
        return new Promise(function (resolve, reject) {
            var reader = new FileReader();
            reader.onload = function () {
                var result = String(reader.result || '');
                var comma = result.indexOf(',');
                resolve(comma >= 0 ? result.slice(comma + 1) : result);
            };
            reader.onerror = function () { reject(reader.error || new Error('FileReader hata')); };
            reader.readAsDataURL(blob);
        });
    }

    window.CalibraPageComments = {
        API_BASE: API_BASE,
        STATUS_LIST: ['approved', 'applied', 'abandoned', 'revised'],
        statusMeta: statusMeta,
        escapeHtml: escapeHtml,
        normalizeKey: normalizeKey,
        fmtDateTime: fmtDateTime,
        uuid: uuid,
        toast: toast,
        getCsrfToken: getCsrfToken,
        apiFetch: apiFetch,
        fileToBase64: fileToBase64,
        imageUrl: function (id) { return API_BASE + '/comment/image/' + encodeURIComponent(id); }
    };
})();
