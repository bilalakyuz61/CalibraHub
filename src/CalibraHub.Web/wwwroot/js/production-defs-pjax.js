/**
 * production-defs-pjax.js
 *
 * Üretim Tanımlamaları sub-tab bar (Personel / Operasyon / Makine / Rota /
 * Aktivite Sebepleri / Vardiya) icin PJAX swap. Tum sayfa yerine sadece
 * #pdt-tab-content alanini fetch + replace eder; ust tab bar persistent kalir,
 * flash yok.
 *
 * KAPSAMA ALANI (2026-06-06 itibarıyla):
 *   [a] Tab bar linkleri (data-pdt-tab) — `_ProductionDefsTabs.cshtml`
 *   [b] SmartBoard header action ("Yeni X") — `window.location.href = url` ile
 *       navigate eden tüm production-defs URL'leri (Location.prototype.href
 *       setter override)
 *   [c] SmartCard primaryAction (kart üzerine tıklama veya Düzenle butonu)
 *   [d] Edit ekranları da `#pdt-tab-content` wrapper'ı içinde, böylece
 *       liste→edit ve edit→liste geçişleri de PJAX'lı.
 *
 * Bilinen URL pattern → tab key + body class mapping:
 *   Edit pattern'leri liste pattern'lerinden ÖNCE gelir
 *   (örn. /Production/PersonnelEdit, /Production/Personnel prefix'iyle eşleşmesin).
 */
(function () {
    'use strict';

    var CONTENT_ID = 'pdt-tab-content';
    var TAB_SELECTOR = 'a[data-pdt-tab]';
    var TABBAR_SELECTOR = '[data-pdt-tabbar]';
    var LOG_PREFIX = '[pdt-pjax]';

    // ---------- URL → tab key + body class inference ----------
    // ÖNEMLİ: Edit pattern'leri liste pattern'lerinden ÖNCE.
    var URL_PATTERNS = [
        { rx: /^\/Production\/PersonnelEdit/i,       key: 'personnel',       bodyClass: 'page-personnel-edit' },
        { rx: /^\/Production\/Personnel(\/|\?|#|$)/i, key: 'personnel',      bodyClass: 'page-personnel' },
        { rx: /^\/Production\/OperationEdit/i,       key: 'operations',      bodyClass: 'page-operation-edit' },
        { rx: /^\/Production\/Operations(\/|\?|#|$)/i, key: 'operations',    bodyClass: 'page-operations' },
        { rx: /^\/Logistics\/MachineEdit/i,          key: 'machines',        bodyClass: 'page-machine-edit' },
        { rx: /^\/Logistics\/Machines(\/|\?|#|$)/i,  key: 'machines',        bodyClass: 'page-machines' },
        { rx: /^\/Production\/RoutingEdit/i,         key: 'routings',        bodyClass: 'page-routing-edit' },
        { rx: /^\/Production\/Routings(\/|\?|#|$)/i, key: 'routings',        bodyClass: 'page-routings' },
        { rx: /^\/Production\/ActivityReasonEdit/i,  key: 'activityreasons', bodyClass: 'page-activity-reason-edit' },
        { rx: /^\/Production\/ActivityReasons(\/|\?|#|$)/i, key: 'activityreasons', bodyClass: 'page-activity-reasons' },
        { rx: /^\/Production\/ShiftEdit/i,           key: 'shifts',          bodyClass: 'page-shift-edit' },
        { rx: /^\/Production\/Shifts(\/|\?|#|$)/i,   key: 'shifts',          bodyClass: 'page-shifts' },
    ];

    function inferFromUrl(rawUrl) {
        if (!rawUrl || typeof rawUrl !== 'string') return null;
        var s = rawUrl.trim();
        // Absolute URL? Strip origin.
        var pathOnly = s;
        if (/^https?:\/\//i.test(s)) {
            try {
                var u = new URL(s);
                if (u.host !== window.location.host) return null;
                pathOnly = u.pathname + u.search + u.hash;
            } catch (e) { return null; }
        }
        if (pathOnly.charAt(0) !== '/') return null;
        for (var i = 0; i < URL_PATTERNS.length; i++) {
            if (URL_PATTERNS[i].rx.test(pathOnly)) {
                return {
                    key: URL_PATTERNS[i].key,
                    bodyClass: URL_PATTERNS[i].bodyClass,
                    path: pathOnly,
                };
            }
        }
        return null;
    }

    // ---------- URL helper (?workspace=1 inject) ----------
    function isWorkspaceFrame() {
        try { return window.self !== window.top; } catch (e) { return false; }
    }
    function ensureWorkspaceParam(url) {
        if (!isWorkspaceFrame() || !url) return url;
        if (url.indexOf('workspace=1') !== -1) return url;
        return url + (url.indexOf('?') !== -1 ? '&' : '?') + 'workspace=1';
    }

    // ---------- Click intercept (tab bar) ----------
    function onClick(e) {
        if (e.button !== 0) return;
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

        var link = e.target && e.target.closest ? e.target.closest(TAB_SELECTOR) : null;
        if (!link) return;
        if (link.getAttribute('target') === '_blank') return;

        if (link.classList.contains('active')) { e.preventDefault(); return; }

        e.preventDefault();
        e.stopPropagation();

        var rawHref = link.getAttribute('href') || '';
        if (!rawHref) return;

        var key = link.getAttribute('data-pdt-tab') || '';
        var newBodyClass = link.getAttribute('data-pdt-body-class') || '';
        var fetchUrl = ensureWorkspaceParam(rawHref);

        navigateTo(fetchUrl, rawHref, key, newBodyClass, true)
            .catch(function (err) {
                console.error(LOG_PREFIX, 'PJAX hata, full nav fallback:', err);
                setHrefRaw(fetchUrl);
            });
    }

    // ---------- Active tab swap ----------
    function setActiveTab(key) {
        var bar = document.querySelector(TABBAR_SELECTOR);
        if (!bar) return;
        var tabs = bar.querySelectorAll(TAB_SELECTOR);
        for (var i = 0; i < tabs.length; i++) {
            var t = tabs[i];
            if (t.getAttribute('data-pdt-tab') === key) t.classList.add('active');
            else t.classList.remove('active');
        }
    }

    // ---------- Body class swap ----------
    var KNOWN_BODY_CLASSES = [
        'page-personnel', 'page-personnel-edit',
        'page-operations', 'page-operation-edit',
        'page-machines', 'page-machine-edit',
        'page-routings', 'page-routing-edit',
        'page-activity-reasons', 'page-activity-reason-edit',
        'page-shifts', 'page-shift-edit',
    ];
    function setBodyPageClass(newClass) {
        var b = document.body;
        for (var i = 0; i < KNOWN_BODY_CLASSES.length; i++) {
            b.classList.remove(KNOWN_BODY_CLASSES[i]);
        }
        if (newClass) b.classList.add(newClass);
    }

    // ---------- Inline script re-execution ----------
    function reExecuteScripts(rootEl) {
        var scripts = rootEl.querySelectorAll('script');
        for (var i = 0; i < scripts.length; i++) {
            var oldS = scripts[i];
            var newS = document.createElement('script');
            for (var j = 0; j < oldS.attributes.length; j++) {
                var a = oldS.attributes[j];
                try { newS.setAttribute(a.name, a.value); } catch (e) { /* ignore */ }
            }
            if (!oldS.src) newS.textContent = oldS.textContent;
            oldS.parentNode.replaceChild(newS, oldS);
        }
    }

    // ---------- Core: fetch + swap ----------
    function navigateTo(fetchUrl, displayUrl, key, newBodyClass, pushHistory) {
        var current = document.getElementById(CONTENT_ID);
        if (!current) {
            // Yapı yok, fallback full nav
            setHrefRaw(fetchUrl);
            return Promise.resolve();
        }

        return fetch(fetchUrl, {
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'pdt-pjax', 'Accept': 'text/html' },
        }).then(function (resp) {
            if (!resp.ok) throw new Error('HTTP ' + resp.status);
            return resp.text();
        }).then(function (html) {
            var parser = new DOMParser();
            var doc = parser.parseFromString(html, 'text/html');
            var fresh = doc.getElementById(CONTENT_ID);
            if (!fresh) throw new Error('#' + CONTENT_ID + ' bulunamadi (server response)');

            // Eski React mount'larını cleanup
            try {
                if (window.CalibraHub && typeof window.CalibraHub.unmountAllInside === 'function') {
                    var n = window.CalibraHub.unmountAllInside(current);
                    if (n > 0) console.debug(LOG_PREFIX, 'unmounted', n, 'root(s)');
                }
            } catch (e) {
                console.warn(LOG_PREFIX, 'unmount hatasi (devam):', e);
            }

            var imported = document.importNode(fresh, true);
            current.parentNode.replaceChild(imported, current);

            // Body class — önce parametre, yoksa wrapper data attr, yoksa URL inference
            var resolvedBodyClass = newBodyClass
                || imported.getAttribute('data-pdt-body-class')
                || (inferFromUrl(displayUrl) || {}).bodyClass
                || '';
            setBodyPageClass(resolvedBodyClass);

            // Inline script'leri re-execute
            try { reExecuteScripts(imported); }
            catch (e) { console.error(LOG_PREFIX, 'script exec hatasi:', e); }

            // Tab active — wrapper data-pdt-key veya URL inference
            var resolvedKey = key
                || imported.getAttribute('data-pdt-key')
                || (inferFromUrl(displayUrl) || {}).key
                || '';
            setActiveTab(resolvedKey);

            if (pushHistory) {
                try {
                    window.history.pushState(
                        { pdt: true, key: resolvedKey, bodyClass: resolvedBodyClass },
                        '',
                        displayUrl
                    );
                } catch (e) { /* ignore */ }
            }

            try {
                var newTitle = doc.querySelector('title');
                if (newTitle && newTitle.textContent) document.title = newTitle.textContent;
            } catch (e) { /* ignore */ }
        });
    }

    // ---------- popstate (browser back/forward) ----------
    function onPopState(e) {
        var s = e.state;
        if (!s || !s.pdt) return;
        var path = window.location.pathname + window.location.search;
        var fetchUrl = ensureWorkspaceParam(path);
        navigateTo(fetchUrl, path, s.key, s.bodyClass, false)
            .catch(function (err) {
                console.error(LOG_PREFIX, 'popstate fail, reload:', err);
                window.location.reload();
            });
    }

    // ---------- Location.href setter override ----------
    // Bunu yapıyoruz çünkü SmartBoard/SmartCard navigateInWorkspace ile
    // `window.location.href = url` yapıyor — `data-pdt-tab` yok, normal
    // click intercept yakalayamaz. Setter'ı sarmalayıp production-defs URL'lerini
    // PJAX'a yönlendiriyoruz.
    var _origHrefSetter = null;
    var _origAssign = null;
    var _origReplace = null;

    function setHrefRaw(url) {
        // PJAX bypass — original setter ile direkt set et (recursive trigger olmasın)
        if (_origHrefSetter) {
            try { _origHrefSetter.call(window.location, url); return; }
            catch (e) { /* fallback */ }
        }
        // Son çare: doğrudan ata (kendi setter'ımız da dahil tüm zincir çalışır)
        window.location.href = url;
    }

    function tryPjaxRoute(rawUrl) {
        if (!document.getElementById(CONTENT_ID)) return false;
        var info = inferFromUrl(rawUrl);
        if (!info) return false;
        var fetchUrl = ensureWorkspaceParam(info.path);
        navigateTo(fetchUrl, info.path, info.key, info.bodyClass, true)
            .catch(function (err) {
                console.warn(LOG_PREFIX, 'setter intercept fail, full nav:', err);
                setHrefRaw(fetchUrl);
            });
        return true;
    }

    function installLocationOverrides() {
        // location.assign
        try {
            _origAssign = window.location.assign.bind(window.location);
            window.location.assign = function (url) {
                if (tryPjaxRoute(String(url))) return;
                _origAssign(String(url));
            };
        } catch (e) { /* sandboxed iframe? */ }

        // location.replace
        try {
            _origReplace = window.location.replace.bind(window.location);
            window.location.replace = function (url) {
                if (tryPjaxRoute(String(url))) return;
                _origReplace(String(url));
            };
        } catch (e) { /* ignore */ }

        // Location.prototype.href setter — workspace-frame-nav-guard zaten override
        // etmiş olabilir (workspace=1 inject için). Biz onun ÜZERİNE wrap ederiz:
        // önce PJAX route deneriz, yoksa orijinal setter (nav-guard) çalışır.
        try {
            var proto = window.Location ? window.Location.prototype : Object.getPrototypeOf(window.location);
            var desc = Object.getOwnPropertyDescriptor(proto, 'href');
            if (desc && desc.configurable && typeof desc.set === 'function') {
                _origHrefSetter = desc.set;  // nav-guard'ın sarmalı (veya native)
                Object.defineProperty(proto, 'href', {
                    configurable: true,
                    enumerable: desc.enumerable,
                    get: desc.get,
                    set: function (url) {
                        var s = String(url);
                        if (tryPjaxRoute(s)) return;
                        _origHrefSetter.call(this, s);
                    },
                });
            }
        } catch (e) {
            console.warn(LOG_PREFIX, 'location.href setter override basarisiz:', e);
        }
    }

    // ---------- Bootstrap ----------
    function init() {
        document.addEventListener('click', onClick, false);
        window.addEventListener('popstate', onPopState);

        installLocationOverrides();

        // İlk sayfa state'i (back/forward için)
        var current = document.getElementById(CONTENT_ID);
        if (current) {
            var key = current.getAttribute('data-pdt-key') || '';
            var bodyClass = current.getAttribute('data-pdt-body-class') || '';
            try {
                window.history.replaceState(
                    { pdt: true, key: key, bodyClass: bodyClass },
                    '',
                    window.location.pathname + window.location.search
                );
            } catch (e) { /* ignore */ }
        }
        console.debug(LOG_PREFIX, 'aktif (URL-pattern intercept ON)');
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
}());
