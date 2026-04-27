/**
 * topPortal.js — Modal'lari en üst pencereye render etmek icin.
 *
 * React Shell iframe'lerinde calisirken modal'lar normalde iframe body'sine
 * portal edilir — bu da `position: fixed` modal'i iframe viewport'una kilitler.
 * Sonuc: modal iframe ortasinda gorunur, ama ekranin tam ortasinda degil
 * (sidebar + tab bar kadar saga kayar).
 *
 * Bu helper, cross-origin izin veriyorsa window.top.document.body'yi dondurur;
 * aksi halde document.body'ye fallback yapar.
 */
export function getTopBody() {
    try {
        if (window.top && window.top.document && window.top.document.body) {
            return window.top.document.body
        }
    } catch (e) { /* cross-origin — fallback */ }
    return document.body
}
