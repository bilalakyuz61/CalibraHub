/**
 * calibra-permissions.js
 *
 * Client-side permission gate. Tek seferlik /Permission/My fetch ile efektif izinleri
 * window.CalibraHub.permissions üzerine yükler; sonrasında senkron sorgu yapılır.
 *
 * Kullanım:
 *   if (CalibraHub.hasPermission('DOCUMENT_NEED', 'CREATE')) { ... }
 *   if (CalibraHub.hasAnyPermission('DOCUMENT_NEED', ['EDIT_OWN','EDIT_ALL'])) { ... }
 *
 * Async loader:
 *   await CalibraHub.loadPermissions();   // login sonrası tek sefer çağrılır
 */
(function (W) {
    'use strict';

    W.CalibraHub = W.CalibraHub || {};
    var cache = null;          // { 'FORM:ACTION': true/false, ... }
    var pendingPromise = null; // in-flight fetch
    var isAdmin = false;

    function buildCache(permissions) {
        var map = {};
        (permissions || []).forEach(function (p) {
            map[(p.formCode + ':' + p.actionCode).toUpperCase()] = !!p.isAllowed;
        });
        return map;
    }

    W.CalibraHub.loadPermissions = function (force) {
        if (cache && !force) return Promise.resolve(cache);
        if (pendingPromise && !force) return pendingPromise;
        pendingPromise = fetch('/Permission/My', { credentials: 'same-origin' })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (!data || !data.ok) throw new Error(data && data.error || 'Yetkiler alınamadı.');
                cache = buildCache(data.permissions);
                // SystemAdmin shortcut: DEFAULT kaynaklı tüm satırlar isAllowed=true ise admin sayalım
                var allDefault = (data.permissions || []).every(function (p) { return p.source === 'DEFAULT' && p.isAllowed; });
                isAdmin = allDefault;
                pendingPromise = null;
                return cache;
            })
            .catch(function (err) {
                pendingPromise = null;
                console.warn('[Permissions] Yüklenemedi, default deny:', err);
                cache = {};
                return cache;
            });
        return pendingPromise;
    };

    W.CalibraHub.hasPermission = function (formCode, actionCode) {
        if (isAdmin) return true;
        if (!cache) return false; // henüz yüklenmedi
        return !!cache[(formCode + ':' + actionCode).toUpperCase()];
    };

    W.CalibraHub.hasAnyPermission = function (formCode, actionCodes) {
        if (isAdmin) return true;
        if (!cache) return false;
        for (var i = 0; i < (actionCodes || []).length; i++) {
            if (cache[(formCode + ':' + actionCodes[i]).toUpperCase()]) return true;
        }
        return false;
    };

    /**
     * Sayfadaki tüm [data-perm-form][data-perm-btn] öğelerini tarar.
     * İzin yoksa disabled kalır + tooltip eklenir; izin varsa enabled hale gelir.
     *
     * Kullanım (HTML convention):
     *   <button data-perm-form="WORK_ORDER_EDIT" data-perm-btn="START" disabled>Başlat</button>
     *
     * Butonlar varsayılan olarak disabled başlar (sunucu tarafı), bu fonksiyon
     * izin varsa enable eder — "disabled→enabled flash" kasıtlı ve daha iyi UX.
     *
     * container: (opsiyonel) DOM root, varsayılan document
     */
    W.CalibraHub.applyButtonPermissions = function (container) {
        var root = container || document;
        var els = root.querySelectorAll('[data-perm-form][data-perm-btn]');
        if (!els.length) return Promise.resolve();
        return W.CalibraHub.loadPermissions().then(function () {
            els.forEach(function (el) {
                var formCode = el.getAttribute('data-perm-form');
                var btnKey   = el.getAttribute('data-perm-btn');
                if (!formCode || !btnKey) return;
                var allowed = W.CalibraHub.hasPermission(formCode, 'BUTTON:' + btnKey);
                if (allowed) {
                    el.disabled = false;
                    el.removeAttribute('data-perm-denied');
                } else {
                    el.disabled = true;
                    el.setAttribute('data-perm-denied', '1');
                    if (!el.hasAttribute('title')) {
                        el.setAttribute('title', 'Bu işlem için yetkiniz yok.');
                    }
                }
            });
        });
    };

    // Otomatik yükle + tüm [data-perm-form][data-perm-btn] butonlarına uygula
    function _autoInit() {
        W.CalibraHub.loadPermissions().then(function () {
            W.CalibraHub.applyButtonPermissions();
        });
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', _autoInit);
    } else {
        _autoInit();
    }
}(window));
