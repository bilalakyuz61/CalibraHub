/* ═══════════════════════════════════════════════════════════════════
   CalibraHub Tarih Alanı Standardı (calibra-datepicker.js)
   ---------------------------------------------------------------
   Sayfadaki TÜM <input type="date"> alanlarını otomatik olarak
   flatpickr'a çevirir (TR locale, görünüm gg.aa.yyyy, değer ISO).
   Yeni ekranlar hiçbir şey yapmadan standarda dahil olur; dinamik
   eklenen inputlar MutationObserver ile yakalanır.

   Davranış sözleşmesi (mevcut ekran kodları kırılmaz):
     • Orijinal input DOM'da kalır (flatpickr type=hidden yapar);
       name/id/value(ISO Y-m-d) aynen çalışır — form post + JS okuma değişmez.
     • Programatik `el.value = '2026-01-01'` atamaları takvime senkronize
       edilir (per-element value interceptor).
     • Takvimden seçimde orijinal input üzerinde 'input' + 'change'
       event'leri tetiklenir — inline oninput/onchange ve addEventListener
       dinleyicileri çalışır.
     • Takvim yalnızca tıklamayla açılır; focus/Tab ile gezinirken açılmaz
       (hızlı veri girişi zinciri bozulmaz). Elle yazım serbest (allowInput).
     • min/max attribute'ları minDate/maxDate olarak taşınır.

   Kapsam dışı (bilinçli):
     • React'in yönettiği inputlar (fiber key'li) dokunulmaz — React'in
       controlled-input değer takibi ile çakışmamak için native kalırlar.
     • data-native-date attribute'u ile ekran bazlı opt-out yapılabilir.
   ═══════════════════════════════════════════════════════════════════ */
(function () {
    'use strict';
    if (window.CalibraDate) return;

    var nativeValueDesc = null;
    try {
        nativeValueDesc = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    } catch (e) { /* eski tarayıcı — interceptor devre dışı kalır */ }

    function isReactManaged(el) {
        var keys = Object.keys(el);
        for (var i = 0; i < keys.length; i++) {
            if (keys[i].indexOf('__react') === 0) return true;
        }
        return false;
    }

    function dispatchNative(el, type) {
        try { el.dispatchEvent(new Event(type, { bubbles: true })); }
        catch (e) {
            var ev = document.createEvent('Event');
            ev.initEvent(type, true, false);
            el.dispatchEvent(ev);
        }
    }

    function enhance(input) {
        if (!input || input.nodeType !== 1 || input.tagName !== 'INPUT') return;
        if (input.type !== 'date') return;
        if (input._flatpickr) return;                       // ekran kendi picker'ını kurmuş
        if (input.hasAttribute('data-native-date')) return; // ekran bazlı opt-out
        if (!window.flatpickr) return;
        if (isReactManaged(input)) return;                  // React controlled input — dokunma

        var locale = (window.flatpickr.l10ns && window.flatpickr.l10ns.tr) || 'default';
        var clickPending = false;

        var cfg = {
            locale: locale,
            dateFormat: 'Y-m-d',
            altInput: true,
            altFormat: 'd.m.Y',
            /* flatpickr altInputClass verildiğinde orijinal class'ları KOPYALAMAZ —
               ekran stillerinin (form-control, sqe-hinput vb.) alt input'ta da
               yaşaması için burada elle taşınır. */
            altInputClass: (input.className ? input.className + ' ' : '') + 'calibra-date-input',
            allowInput: true,
            disableMobile: true,
            appendTo: document.body,
            onOpen: function (dates, dateStr, instance) {
                /* Mousedown ile işaretlenmediyse focus/Tab ile açıldı — kapat */
                if (!clickPending) { instance.close(); return; }
                clickPending = false;
            },
            onChange: function () {
                /* İnline oninput ve programatik dinleyiciler için orijinal
                   input'a input event'i yansıt (change'i flatpickr zaten atar) */
                dispatchNative(input, 'input');
            }
        };
        var minAttr = input.getAttribute('min');
        var maxAttr = input.getAttribute('max');
        if (minAttr) cfg.minDate = minAttr;
        if (maxAttr) cfg.maxDate = maxAttr;

        var fp;
        try { fp = window.flatpickr(input, cfg); }
        catch (e) { return; }
        if (!fp || !fp.altInput) return;

        fp.altInput.addEventListener('mousedown', function () {
            clickPending = true;
            /* 200 ms içinde open gelmezse izni temizle */
            setTimeout(function () { clickPending = false; }, 200);
        });
        /* placeholder/required/disabled'ı flatpickr kendisi kopyalar; readOnly kalır */
        if (input.readOnly) fp.altInput.readOnly = true;

        /* Programatik `el.value = ...` atamalarını takvim + görünen input'a senkle.
           Guard: setDate kendisi de value yazar — sonsuz döngüyü _calibraSyncing keser. */
        if (nativeValueDesc && nativeValueDesc.set && nativeValueDesc.get) {
            try {
                Object.defineProperty(input, 'value', {
                    configurable: true,
                    get: function () { return nativeValueDesc.get.call(input); },
                    set: function (v) {
                        nativeValueDesc.set.call(input, v == null ? '' : v);
                        if (input._flatpickr && !input._calibraSyncing) {
                            input._calibraSyncing = true;
                            try { input._flatpickr.setDate(v || null, false); } catch (e) { /* yoksay */ }
                            input._calibraSyncing = false;
                        }
                    }
                });
            } catch (e) { /* defineProperty engellendiyse senkron atlanır */ }
        }
    }

    function scan(root) {
        if (!root) return;
        if (root.nodeType === 1) {
            if (root.matches && root.matches('input[type="date"]')) enhance(root);
            if (root.querySelectorAll) {
                var list = root.querySelectorAll('input[type="date"]');
                for (var i = 0; i < list.length; i++) enhance(list[i]);
            }
        } else if (root === document) {
            var all = document.querySelectorAll('input[type="date"]');
            for (var j = 0; j < all.length; j++) enhance(all[j]);
        }
    }

    function start() {
        scan(document);
        /* PJAX sekme geçişleri, modal açılışları, dinamik formlar */
        var mo = new MutationObserver(function (mutations) {
            for (var m = 0; m < mutations.length; m++) {
                var added = mutations[m].addedNodes;
                for (var n = 0; n < added.length; n++) {
                    if (added[n].nodeType === 1) scan(added[n]);
                }
            }
        });
        mo.observe(document.documentElement, { childList: true, subtree: true });
    }

    window.CalibraDate = {
        enhance: enhance,
        scan: scan,
        /* Tarih atamak için güvenli helper — flatpickr'lı/flatpickr'sız fark etmez */
        setValue: function (elOrId, isoValue) {
            var el = typeof elOrId === 'string' ? document.getElementById(elOrId) : elOrId;
            if (!el) return;
            if (el._flatpickr) el._flatpickr.setDate(isoValue || null, false);
            else el.value = isoValue || '';
        }
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
