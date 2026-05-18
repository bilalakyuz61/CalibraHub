/**
 * Otomatik ilk input fokus (rapor §2.8 cozumu).
 *
 * Edit form'larda kullanici sayfayi acinca ilk veri girisi input'una otomatik
 * focus verir. Manuel tıklama gerekmeden klavye ile veri girisine baslanabilir.
 *
 * Devre disi birakmak icin: data-autofocus="off" attribute ekle (formda veya body'de).
 * Belirli input'i sec: data-autofocus="true" attribute ekle (manuel override).
 *
 * Davranis:
 *   1. data-autofocus="true" varsa: o input'a focus
 *   2. Yoksa: <form> icindeki ilk visible/enabled input'a focus
 *      Disari kalanlar: type="hidden", disabled, readonly, type="submit", type="button",
 *      data-autofocus="off"
 *   3. Modal/dialog acikken devre disi (modal kendi autofocus'unu yonetir)
 */
(function () {
    'use strict';

    function isFocusable(el) {
        if (!el) return false;
        if (el.disabled || el.readOnly) return false;
        if (el.hidden || el.type === 'hidden') return false;
        if (el.dataset && el.dataset.autofocus === 'off') return false;
        var skipTypes = ['submit', 'button', 'reset', 'image', 'file'];
        if (skipTypes.indexOf((el.type || '').toLowerCase()) >= 0) return false;
        // Visible check
        var rect = el.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) return false;
        var style = window.getComputedStyle(el);
        if (style.display === 'none' || style.visibility === 'hidden') return false;
        return true;
    }

    function findFirstInput() {
        // Once explicit override
        var explicit = document.querySelector('[data-autofocus="true"]');
        if (explicit && isFocusable(explicit)) return explicit;

        // Sonra ilk form'un ilk uygun input/select/textarea'si
        var forms = document.querySelectorAll('form, [role="form"]');
        for (var i = 0; i < forms.length; i++) {
            var form = forms[i];
            if (form.dataset && form.dataset.autofocus === 'off') continue;
            var candidates = form.querySelectorAll('input, select, textarea');
            for (var j = 0; j < candidates.length; j++) {
                if (isFocusable(candidates[j])) return candidates[j];
            }
        }

        // Form yoksa sayfanin ilk uygun input'u (Login/Setup gibi form etiketi olmayan sayfalar)
        var all = document.querySelectorAll('input, select, textarea');
        for (var k = 0; k < all.length; k++) {
            if (isFocusable(all[k])) return all[k];
        }
        return null;
    }

    function run() {
        // Modal acikken atla
        if (document.querySelector('.modal.show, .sqe-lookup-modal[open], dialog[open]')) return;
        // Body opt-out
        if (document.body.dataset && document.body.dataset.autofocus === 'off') return;

        var input = findFirstInput();
        if (input) {
            try {
                input.focus({ preventScroll: false });
                if (typeof input.select === 'function' && input.type !== 'date'
                    && input.type !== 'datetime-local') {
                    // Metin alanlarinda mevcut deger varsa secili gelir → hizli replace
                    input.select();
                }
            } catch (_) { /* swallow */ }
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', run);
    } else {
        // Kucuk gecikme: React mount + Razor view init tamamlansin
        setTimeout(run, 50);
    }
})();
