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

/**
 * getBoundingClientRect() bir DOM node'un KENDI dokumaninin (document/window)
 * viewport'una gore konumunu doner. Shell iframe tab mimarisinde (Shell.jsx:
 * Sidebar + TabBar disinda, body alani iframe'lerdir) bir butonu iframe icinden
 * olcup, sonucu getTopBody() ile window.top govdesine portallanmis bir elemente
 * `position:fixed` olarak uygularsan, iframe'in top pencere icindeki offset'i
 * (sidebar genisligi + header/tab-bar yuksekligi) HESABA KATILMAZ — menu butonun
 * gercek ekran konumundan o kadar sola/yukari kaymis gorunur (2026-07-21,
 * PageComment Seq 19 — SmartTableRow.jsx "Islemler" dropdown'unun butonun sag-
 * altina degil, sidebar/tab-bar kadar yanlis bir noktaya acilmasinin kok nedeni).
 *
 * Bu fonksiyon, mevcut pencereden window.top'a kadar (nested iframe'ler dahil —
 * ör. _DesignRulesTabs sabit-serit+ic-iframe deseni) her seviyenin frameElement
 * dikdortgenini toplayarak kumulatif {x,y} offset'i doner. Ayni-kaynak degilse
 * (cross-origin) o ana kadar birikmis offset ile durur — getTopBody() ile ayni
 * savunmaci desen.
 *
 * @returns {{x:number,y:number}}
 */
export function getTopFrameOffset() {
    var x = 0, y = 0
    try {
        var w = window
        while (w !== w.top && w.frameElement) {
            var r = w.frameElement.getBoundingClientRect()
            x += r.left
            y += r.top
            w = w.parent
        }
    } catch (e) { /* cross-origin — o ana kadar birikmis offset ile devam */ }
    return { x: x, y: y }
}

/**
 * window.top'un viewport boyutu — getTopFrameOffset() ile aynı senaryonun
 * tamamlayıcısı: bir eleman window.top govdesine portallanip position:fixed
 * ile konumlandırılıyorsa, "ekrana taşmasın" clamp hesabı da iframe'in kendi
 * (daha küçük) innerWidth/innerHeight'ı değil, window.top'unki ile yapılmalı.
 * Cross-origin/erisilemez durumda mevcut pencerenin boyutuna duser.
 *
 * @returns {{width:number,height:number}}
 */
export function getTopViewportSize() {
    try {
        if (window.top && typeof window.top.innerWidth === 'number') {
            return { width: window.top.innerWidth, height: window.top.innerHeight }
        }
    } catch (e) { /* cross-origin — fallback */ }
    return { width: window.innerWidth, height: window.innerHeight }
}
