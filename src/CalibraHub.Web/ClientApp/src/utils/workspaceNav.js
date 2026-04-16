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
