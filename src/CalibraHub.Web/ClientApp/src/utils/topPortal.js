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
import { useEffect, useRef } from 'react'

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

/**
 * Bu pencereden (iframe) window.top'a kadar frame zincirinde HERHANGI bir
 * seviyenin iframe elementi gorunmez mi (display:none → sifir olculu rect)
 * kontrol eder. Shell workspace-tab mimarisinde (Shell.jsx ~satir 1035:
 * `display: (!showDashboard && t.key === activeTabKey) ? 'block' : 'none'`)
 * kullanici baska bir sekmeye/sol menuye gecince aktif OLMAYAN workspace-tab
 * iframe'i display:none olur — ama o iframe'in ICINDEKI React state YASAMAYA
 * DEVAM EDER; eger bir dropdown/modal window.top govdesine PORTALLANMISSA
 * (bkz. getTopBody), o portal icerigi iframe ile birlikte gizlenmez — ekranda
 * "yetim" gorunur kalir (2026-07-23, PageComment Seq 25 — SmartTableRow
 * "Islemler" menusu sekme/sayfa degisince acik kaliyordu).
 *
 * getTopFrameOffset() ile AYNI frame-yürüme desenini kullanir (nested
 * iframe'ler dahil, ör. _DesignRulesTabs sabit-serit+ic-iframe deseni);
 * cross-origin'de olcemedigi noktada GORUNUR varsayar (agresif kapanmayi
 * degil, sessiz no-op'u tercih eder — ayni savunmaci desen).
 *
 * @returns {boolean}
 */
export function isTopFrameVisible() {
    try {
        var w = window
        while (w !== w.top && w.frameElement) {
            var rect = w.frameElement.getBoundingClientRect()
            if (rect.width <= 0 || rect.height <= 0) return false
            w = w.parent
        }
    } catch (e) { /* cross-origin — olcemiyoruz, gorunur varsay */ return true }
    return true
}

/**
 * window.top govdesine (getTopBody) portallanmis bir dropdown/modal'in iki
 * "sessiz yetim kalma" senaryosuna karsi kendini kapatmasini saglayan ortak
 * hook (2026-07-23, PageComment Seq 25):
 *
 *   1) Iframe ICINDE hard navigasyon (kullanici baska bir sayfaya gecer) —
 *      dokuman/JS, React'in yeniden render olup portal'i kaldirmasina firsat
 *      bulamadan yikilabilir. `pagehide` senkron calisir; burada hem state
 *      kapatilir hem de (React'a zaman kalmama ihtimaline karsi) portallanmis
 *      DOM node'u nodeRef uzerinden DOGRUDAN top dokumandan sokulur. React'in
 *      kendisi de ayni node'u kaldirmayi denerse (state guncellemesi
 *      navigasyondan once yetisirse) try/catch bunu yutar — node zaten yok,
 *      ikinci kaldirma denemesi zararsizdir/beklenendir.
 *
 *   2) Shell sekme/sol menu ile BASKA TAB'a (veya Dashboard'a) gecis — iframe
 *      yasamaya devam eder ama display:none olur; portal top dokumaninda
 *      GORUNUR kalir. Shell bu gecis icin iframe'e bir event yayinlamadigi
 *      icin (sadece calibra:init / calibra:hotkey postMessage'lari var) kisa
 *      araliki poll ile isTopFrameVisible() kontrol edilir; gizlenince state
 *      kapatilir — dokuman canli oldugu icin React normal reconciliation ile
 *      portal cocuklarini kendisi kaldirir (burada RAW DOM mudahalesi YOK,
 *      aksi halde React'in kendi kaldirma adimiyla catisir).
 *
 * @param {boolean} isOpen - portal icerigi acik mi (menuOpen/confirmOpen/vb.)
 * @param {Function} onClose - kapatma handler'i (ör. `function(){setXOpen(false)}`
 *   veya onu saran bir "iptal" fonksiyonu — SmartCard'daki handleConfirmNo gibi
 *   callback ref'ini de temizleyen). Her render'da yeniden olusturulabilir —
 *   hook, en guncel referansi bir ref uzerinden okur; bu yuzden dependency
 *   olarak eklenmez ve gereksiz yeniden-abone-olmaya yol acmaz.
 * @param {{current: (Node|null)}} [nodeRef] - portallanmis DOM node'un ref'i
 *   (createPortal'a verilen ust-duzey elemente baglanir); verilmezse sadece
 *   state kapatilir, raw DOM adimi atlanir.
 */
export function useClosePortalOnFrameHidden(isOpen, onClose, nodeRef) {
    var onCloseRef = useRef(onClose)
    onCloseRef.current = onClose

    useEffect(function () {
        if (!isOpen) return undefined

        function forceCloseForNavigation() {
            onCloseRef.current()
            var node = nodeRef && nodeRef.current
            if (!node || !node.parentNode) return
            try { node.parentNode.removeChild(node) } catch (e) { /* React zaten kaldirmis olabilir — yut */ }
        }

        window.addEventListener('pagehide', forceCloseForNavigation)
        var pollId = setInterval(function () {
            if (!isTopFrameVisible()) onCloseRef.current()
        }, 400)

        return function () {
            window.removeEventListener('pagehide', forceCloseForNavigation)
            clearInterval(pollId)
        }
        // onCloseRef ile guncel tutuldugu icin onClose deps'te yok; nodeRef bir
        // useRef nesnesi oldugu icin (kimligi sabit) deps'e eklemeye gerek yok.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isOpen])
}
