/**
 * workspaceNav.js — Paylasilmis workspace navigasyon utility.
 *
 * Tum React bilesenleri icin standart navigasyon fonksiyonu.
 * `window.location.href = url` yerine bu kullanilmali.
 *
 * Strateji (savunma katmanlari):
 *   Kat 1 — Bu fonksiyon: iframe tespiti + url duzeltmesi
 *   Kat 2 — workspace-frame-nav-guard.js: Location.prototype override (global catch-all)
 *   Kat 3 — WorkspaceRedirectPreservationMiddleware: redirect response'larini duzeltir
 */

/**
 * Verilen URL'e workspace frame icindeyken otomatik olarak ?workspace=1 ekler.
 * iframe disinda (normal sayfa) hic bir degisiklik yapilmaz.
 *
 * @param {string} url - Navigasyon yapilacak URL
 */
export function navigateInWorkspace(url) {
    if (!url) return;

    // iframe icinde miyiz?
    var inIframe = (function () {
        try { return window.self !== window.top; } catch (e) { return true; }
    })();

    if (inIframe && url.indexOf('workspace=1') === -1) {
        url = url + (url.indexOf('?') !== -1 ? '&' : '?') + 'workspace=1';
    }

    window.location.href = url;
}

/**
 * URL'in workspace=1 parametresi icerip icermedigini kontrol eder.
 * @param {string} url
 * @returns {boolean}
 */
export function hasWorkspaceFlag(url) {
    return typeof url === 'string' && url.indexOf('workspace=1') !== -1;
}

/**
 * URL'in path kismini (query ve hash haric) cikarir. SmartBoard aksiyonlari
 * `openInTab` ile ayri sekmede acilirken `matchPath` acikca verilmemisse
 * (undefined — alan hic gonderilmemis), "bu sayfa zaten acik bir sekmede mi"
 * kontrolu icin path buradan otomatik turetilir (bkz. CLAUDE.md workspace-tabs
 * GENEL kurali, 2026-07-21 — SmartCard.jsx/SmartTableRow.jsx dispatchActionUrl
 * bu fonksiyonu kullanir). Hem relative ("/AuditLog?entity=Item") hem absolute
 * ("https://host/AuditLog?entity=Item") url'leri destekler; window.location.href
 * taban alinarak URL API ile cozulur.
 *
 * Bilerek `matchPath: null`/`false` verilmis "her tiklamada yeni sekme ac"
 * istisnalari bu fonksiyonun DISINDA ele alinir — caller, alan acikca
 * (undefined disinda bir deger ile) verilmisse bu fonksiyonu hic cagirmamali.
 *
 * @param {string} url
 * @returns {string|null} path, veya turetilemiyorsa null (caller eski
 *   davranisa — sadece exact-url eslesmesine — duser).
 */
export function deriveMatchPathFromUrl(url) {
    if (typeof url !== 'string' || !url) return null;
    if (url.charAt(0) === '#') return null; // hash-only URL'de path yok

    try {
        var path = new URL(url, window.location.href).pathname;
        return path || null;
    } catch (e) {
        // URL constructor cok nadir durumda basarisiz olursa string-tabanli fallback:
        // '?' ve '#'den once kesilen kisim path kabul edilir.
        var cut = url;
        var qIdx = cut.indexOf('?');
        if (qIdx !== -1) cut = cut.slice(0, qIdx);
        var hIdx = cut.indexOf('#');
        if (hIdx !== -1) cut = cut.slice(0, hIdx);
        return cut || null;
    }
}
