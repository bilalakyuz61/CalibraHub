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
     • Değer değiştiğinde — takvimden seçimle YA DA elle yazıp blur/Enter ile
       — orijinal input üzerinde 'input' + 'change' event'leri tetiklenir;
       inline oninput/onchange ve addEventListener dinleyicileri çalışır.
     • min/max attribute'ları minDate/maxDate olarak taşınır.

   Tıklama/yazma davranışı (2026-07-23 karar — PageComment Seq 24):
     • Takvim YALNIZCA sağ kenardaki takvim ikonuna tıklanınca açılır
       (clickOpens:false + ikon bölgesinde click-zone tespiti, bkz.
       isIconZoneHit). Alanın metin kısmına tıklamak/odaklanmak takvimi
       AÇMAZ — kullanıcı elle de gösterim formatında (gg.aa.yyyy) tarih
       yazabilir; yazım blur veya Enter ile "commit" edilir.
     • Elle girilen metin flatpickr'ın kendi allowInput parse'ı ile
       (altFormat = d.m.Y) çözümlenir. Parse başarısız olursa flatpickr
       alanı kendiliğinden boşaltır — bu dosya son geçerli değere GERİ
       DÖNER (alan sessizce boşa düşmez) ve kısa kırmızı halka animasyonu
       (.cdp-invalid-flash) ile kullanıcıyı uyarır. Alanı BİLEREK
       boşaltmak (tüm metni silip çıkmak) geçerli bir durumdur, geri
       alınmaz.
     • F4 / Alt+ArrowDown odaklıyken takvimi açar (fare olmadan da
       erişilebilir olsun diye — ikonun kendisi ayrı bir DOM elemanı
       olmadığı için tab ile hedeflenemiyor).

   Kapsam dışı (bilinçli):
     • React'in yönettiği inputlar (fiber key'li) dokunulmaz — React'in
       controlled-input değer takibi ile çakışmamak için native kalırlar.
     • data-native-date attribute'u ile ekran bazlı opt-out yapılabilir.
     • Warehouse/StockDocEdit.cshtml + Warehouse/InventoryEdit.cshtml kendi
       bespoke flatpickr kurulumuna sahip (input type="text", ayrı ISO/
       gösterim alan ayrımı yok) — bu enhancer'ın taradığı
       input[type="date"] kapsamının dışında kalırlar, dokunulmadı.
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

    /* Sağ kenardaki takvim ikonu bölgesine mi tıklandı/mouse üzerinde mi?
       İkon her zaman sağ 10px + 15px genişlik (calibra-datepicker.css:
       background-position:right 10px, size:15px) → fromRight ~10-25px'i kaplar.
       Tıklama bölgesi EN AZ ~28px olmalı ki ikonun tam üzeri çalışsın.
       2026-08-06 fix: yalnız computed padding-right'a bağlanınca, ekranların genel
       input padding'i (ör. .sqe-hinput `padding:9px 12px`) date altInput'un
       padding-right'ını 12px'e ezip bölgeyi daraltıyordu → ikonun üzeri değil yalnız
       sağ kenarı tetikliyordu. Math.max ile 28px taban veririz; bilinçli daha geniş
       ikon boşluğu olan ekranlarda (padding-right > 28) o değer korunur. */
    function isIconZoneHit(el, clientX) {
        var rect = el.getBoundingClientRect();
        var pr = parseFloat(window.getComputedStyle(el).paddingRight) || 0;
        var zone = Math.max(pr, 28);
        var fromRight = rect.right - clientX;
        return fromRight >= 0 && fromRight <= zone;
    }

    function enhance(input) {
        if (!input || input.nodeType !== 1 || input.tagName !== 'INPUT') return;
        if (input.type !== 'date') return;
        if (input._flatpickr) return;                       // ekran kendi picker'ını kurmuş
        if (input.hasAttribute('data-native-date')) return; // ekran bazlı opt-out
        if (!window.flatpickr) return;
        if (isReactManaged(input)) return;                  // React controlled input — dokunma

        var locale = (window.flatpickr.l10ns && window.flatpickr.l10ns.tr) || 'default';

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
            /* Takvim yalnızca sağ kenar ikonuna tıklanınca açılır (bkz. aşağıdaki
               click dinleyicisi + isIconZoneHit) — flatpickr'ın kendi click/focus
               ile aç davranışı tamamen kapalı, elle yazım serbest kalır. */
            clickOpens: false,
            disableMobile: true,
            appendTo: document.body,
            onChange: function () {
                /* Takvimden seçim — orijinal input'a input+change yansıt
                   (inline oninput/onchange ve addEventListener dinleyicileri için). */
                dispatchNative(input, 'input');
                dispatchNative(input, 'change');
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

        /* placeholder/required/disabled'ı flatpickr kendisi kopyalar; readOnly kalır */
        if (input.readOnly) fp.altInput.readOnly = true;

        /* ── Sağ kenar ikonuna tıklama → takvimi aç/kapat. Metin bölgesine
           tıklamak yalnızca caret konumlar (readOnly değilse yazmaya devam
           edilebilir). readOnly alanlarda da ikon tıklaması takvimi açar
           (önceki davranışla tutarlı — readOnly serbest yazımı değil,
           seçim yoluyla değer girişini engellemez). ──────────────────── */
        fp.altInput.addEventListener('click', function (e) {
            if (fp.altInput.disabled) return;
            if (isIconZoneHit(fp.altInput, e.clientX)) fp.toggle();
        });
        /* Fare ikon bölgesindeyken pointer, metin bölgesinde text imleci
           göster — alan artık "her yeri tıkla-aç" değil "yaz ya da ikona
           tıkla" olduğu için imleç bunu yansıtmalı. */
        fp.altInput.addEventListener('mousemove', function (e) {
            if (fp.altInput.disabled || fp.altInput.readOnly) return;
            fp.altInput.style.cursor = isIconZoneHit(fp.altInput, e.clientX) ? 'pointer' : 'text';
        });
        /* Klavye erişilebilirliği — ikonun kendisi ayrı bir odaklanabilir
           DOM elemanı olmadığından F4 / Alt+ArrowDown ile açılabilsin
           (Windows combobox/native date-input konvansiyonu). */
        fp.altInput.addEventListener('keydown', function (e) {
            var key = e.key || '';
            if (key === 'F4' || (e.altKey && key === 'ArrowDown')) {
                e.preventDefault();
                fp.toggle();
            }
        });

        /* ── Elle yazım güvenliği ────────────────────────────────────────
           flatpickr'ın kendi blur/Enter işleyicisi (allowInput) parse
           başarısız olduğunda alanı SESSİZCE boşaltır (hem görünen hem
           gizli input, doğrulandı: flatpickr.min.js setDate → 0 seçili
           tarih → clear()). Bu, kullanıcının önceki geçerli tarihini
           kaybettirir. Aşağıdaki 'blur' dinleyicisi flatpickr'ın KENDİ
           blur dinleyicisinden SONRA çalışır (aynı elemana sonradan
           eklenen dinleyiciler DOM ekleme sırasına göre tetiklenir) ve üç
           durumu ayırt eder:
             1) Kullanıcı hiçbir şey yazmadı (odaklanıp çıktı)      → no-op
             2) Kullanıcı alanı BİLEREK boşalttı VEYA geçerli bir
                tarih yazdı                                        → kabul et + event yay
             3) Kullanıcı geçersiz/eksik bir şey yazdı              → son
                geçerli değere geri dön + kısa görsel uyarı (alan boşa
                düşmez). Enter tuşu da flatpickr içinde senkron blur()
                çağırdığı için aynı yoldan geçer, ayrı işleyici gerekmez. */
        var lastValidIso = input.value || '';
        var typedSinceFocus = false;
        var lastTypedRaw = '';
        fp.altInput.addEventListener('focus', function () {
            lastValidIso = input.value || '';
            typedSinceFocus = false;
        });
        fp.altInput.addEventListener('input', function () {
            /* Gerçek klavye/paste girişinde tetiklenir; programatik
               setDate/value ataması native 'input' event'i doğurmaz. */
            typedSinceFocus = true;
            lastTypedRaw = fp.altInput.value;
        });
        fp.altInput.addEventListener('blur', function () {
            if (!typedSinceFocus) return;
            typedSinceFocus = false;
            var wasEmptied = lastTypedRaw.trim() === '';
            var newIso = input.value || '';
            if (wasEmptied || newIso) {
                /* Bilinçli boşaltma ya da başarılı parse — yeni durumu kabul et. */
                if (newIso !== lastValidIso) {
                    dispatchNative(input, 'input');
                    dispatchNative(input, 'change');
                }
                lastValidIso = newIso;
                return;
            }
            /* raw doluydu ama parse edilemedi (flatpickr alanı boşalttı) — geri al. */
            if (lastValidIso) {
                try { fp.setDate(lastValidIso, false); } catch (e) { /* yoksay */ }
            }
            fp.altInput.classList.add('cdp-invalid-flash');
            setTimeout(function () { fp.altInput.classList.remove('cdp-invalid-flash'); }, 550);
        });

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
