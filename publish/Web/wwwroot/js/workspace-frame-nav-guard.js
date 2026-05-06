/**
 * workspace-frame-nav-guard.js
 *
 * CalibraHub Shell'in iframe tab'larinda otomatik olarak yuklenir.
 * Her tur navigasyona ?workspace=1 parametresini ekler.
 *
 * Kapsama alani:
 *   [1] <a href="..."> link tiklama olaylari
 *   [2] window.location.assign(url)
 *   [3] window.location.replace(url)
 *   [4] window.location.href = url  (Location.prototype setter override)
 *   [5] history.pushState / replaceState
 */
(function (W) {
    'use strict';

    if (W.self === W.top) return;

    var WORKSPACE_PARAM = 'workspace=1';
    var HOST = W.location.host;

    function ensureWorkspace(url) {
        if (!url || typeof url !== 'string') return url;
        var t = url.trim();
        if (t.charAt(0) === '#') return url;
        if (/^(javascript|mailto|tel):/i.test(t)) return url;
        if (/^(https?:)?\/\//i.test(t)) {
            if (t.indexOf(HOST) === -1) return url;
        }
        // Reverse-proxy ile sunulan harici servislere (ornegin Grafana) workspace
        // parametresi eklenmez — bu servisler kendi SPA router'larini yonetir.
        if (/(^|\/)grafana(\/|$|\?)/i.test(t)) return url;
        if (url.indexOf(WORKSPACE_PARAM) !== -1) return url;
        return url + (url.indexOf('?') !== -1 ? '&' : '?') + WORKSPACE_PARAM;
    }

    // [1] <a href> klik yakalayici
    document.addEventListener('click', function (e) {
        var el = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (!el) return;
        if (el.getAttribute('data-workspace-ignore') !== null) return;
        var tgt = el.getAttribute('target');
        if (tgt && tgt !== '_self' && tgt !== '') return;
        var rawHref = el.getAttribute('href') || '';
        if (!rawHref || rawHref.charAt(0) === '#') return;
        var resolved = el.href || rawHref;
        var fixed = ensureWorkspace(resolved);
        if (fixed === resolved) return;
        e.preventDefault();
        e.stopPropagation();
        W.location.href = fixed;
    }, true);

    // [2] location.assign
    try {
        var _origAssign = W.location.assign.bind(W.location);
        W.location.assign = function (url) { _origAssign(ensureWorkspace(String(url))); };
    } catch (_) {}

    // [3] location.replace
    try {
        var _origReplace = W.location.replace.bind(W.location);
        W.location.replace = function (url) { _origReplace(ensureWorkspace(String(url))); };
    } catch (_) {}

    // [4] Location.prototype.href setter
    try {
        var _proto = W.Location ? W.Location.prototype : Object.getPrototypeOf(W.location);
        var _desc = Object.getOwnPropertyDescriptor(_proto, 'href');
        if (_desc && _desc.configurable && typeof _desc.set === 'function') {
            var _origSetter = _desc.set;
            Object.defineProperty(_proto, 'href', {
                configurable: true,
                enumerable: _desc.enumerable,
                get: _desc.get,
                set: function (url) {
                    _origSetter.call(this, ensureWorkspace(String(url)));
                },
            });
            W.__wsNavHrefOverride = true;
        }
    } catch (_) {
        W.__wsNavHrefOverride = false;
    }

    // [5] history.pushState / replaceState
    try {
        var _origPush = W.history.pushState.bind(W.history);
        W.history.pushState = function (s, t, url) {
            _origPush(s, t, url != null ? ensureWorkspace(String(url)) : url);
        };
        var _origRepl = W.history.replaceState.bind(W.history);
        W.history.replaceState = function (s, t, url) {
            _origRepl(s, t, url != null ? ensureWorkspace(String(url)) : url);
        };
    } catch (_) {}

    W.__wsNavGuardActive = true;
    console.debug('[CalibraHub] workspace-frame-nav-guard active | href-override:', W.__wsNavHrefOverride);

}(window));
