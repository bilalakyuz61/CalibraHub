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
       (altFormat = d.m.Y) çözümlenir; ANCAK blur/Enter'da bu dosya AYRICA
       kendi katı takvim doğrulamasını (parseStrictDMY) yapar — flatpickr'ın
       JS Date aritmetiği taşan gün/ay değerlerini (32.13.2026 gibi) SESSİZCE
       başka bir geçerli tarihe yuvarlayabildiği için flatpickr'ın "parse
       başarılı" sonucuna tek başına güvenilmez. Katı doğrulama başarısız
       olursa (regex uymuyor VEYA ay/gün takvim dışı) bu dosya son geçerli
       değere GERİ DÖNER (alan sessizce yanlış/boş değerde kalmaz) ve kısa
       kırmızı halka animasyonu (.cdp-invalid-flash) ile kullanıcıyı uyarır.
       Alanı BİLEREK boşaltmak (tüm metni silip çıkmak) geçerli bir durumdur,
       geri alınmaz.
     • GİRİŞ MASKESİ (PageComment Seq 1095): altInput'a yazarken yalnızca
       rakam ve nokta kabul edilir (keydown'da harf/sembol preventDefault);
       her 'input' event'inde ham değer basamaklara indirgenip (getDigits,
       max 8 hane) dd.aa.yyyy kalıbına yeniden dizilir (formatDigits) — 2. ve
       4. haneden sonra nokta OTOMATİK eklenir, imleç basamak konumuna göre
       yeniden konumlanır. Backspace/Delete bir noktanın üzerine denk
       gelirse noktayla birlikte bitişik haneyi de siler (yoksa nokta anında
       geri gelip silme işlemini görünmez kılar). Yapıştırma (paste) da aynı
       'input' event'inden geçtiği için otomatik ayıklanıp maskelenir.
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

    /* ── Giriş maskesi yardımcıları (PageComment Seq 1095) ──────────────
       Tamamen basamak-güdümlü: nokta karakterleri asla "veri" sayılmaz,
       her yeniden biçimlendirmede ham metinden basamaklar çıkarılır ve
       dd.aa.yyyy kalıbına göre yeniden diziliyor. Bu yüzden yapıştırma,
       IME girişi ve elle yazılan nokta/ayraç farkı gözetmeksizin aynı
       yoldan geçer — tek kaynak basamak dizisidir. ────────────────────── */
    function getDigits(str) {
        return (str || '').replace(/\D/g, '').slice(0, 8);
    }
    function formatDigits(digits) {
        var out = digits.slice(0, 2);
        if (digits.length > 2) out += '.' + digits.slice(2, 4);
        if (digits.length > 4) out += '.' + digits.slice(4, 8);
        return out;
    }
    /* Ham metni (yapıştırma dahil) dd.aa.yyyy maskesine göre yeniden
       biçimlendirir; imleci, biçimlendirme sonrası aynı basamak konumunda
       tutar (ör. imleç 2 basamaktan sonraysa, otomatik nokta eklense de
       imleç noktadan sonra kalır). */
    function maskReformat(altInput) {
        var raw = altInput.value;
        var caret = altInput.selectionStart;
        if (caret == null) caret = raw.length;
        var digitsBeforeCaret = 0;
        for (var i = 0; i < caret && i < raw.length; i++) {
            if (/\d/.test(raw.charAt(i))) digitsBeforeCaret++;
        }
        var formatted = formatDigits(getDigits(raw));
        altInput.value = formatted;
        var newCaret = formatted.length;
        if (digitsBeforeCaret === 0) {
            newCaret = 0;
        } else {
            var count = 0;
            for (var j = 0; j < formatted.length; j++) {
                if (/\d/.test(formatted.charAt(j))) {
                    count++;
                    if (count === digitsBeforeCaret) { newCaret = j + 1; break; }
                }
            }
        }
        try { altInput.setSelectionRange(newCaret, newCaret); } catch (e) { /* yoksay */ }
        return formatted;
    }
    /* Katı takvim doğrulaması — flatpickr'ın kendi parse'ına GÜVENMEDEN
       çalışır (JS Date aritmetiği taşan gün/ay değerlerini sessizce başka
       bir geçerli tarihe yuvarlayabilir, ör. 32.01.2026 → 01.02.2026).
       gg/aa/yyyy tam olarak takvimde var olan bir tarihi ifade etmiyorsa
       null döner. Geçerliyse 'yyyy-mm-dd' (ISO) döner. */
    function parseStrictDMY(raw) {
        var m = /^(\d{2})\.(\d{2})\.(\d{4})$/.exec((raw || '').trim());
        if (!m) return null;
        var dd = parseInt(m[1], 10), mm = parseInt(m[2], 10), yyyy = parseInt(m[3], 10);
        if (mm < 1 || mm > 12) return null;
        var daysInMonth = new Date(yyyy, mm, 0).getDate(); /* mm 1-indexed + day 0 = ayın son günü */
        if (dd < 1 || dd > daysInMonth) return null;
        var mmStr = mm < 10 ? '0' + mm : '' + mm;
        var ddStr = dd < 10 ? '0' + dd : '' + dd;
        return yyyy + '-' + mmStr + '-' + ddStr;
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

        /* ── Giriş maskesi (PageComment Seq 1095) ────────────────────────
           1) keydown: rakam/nokta dışındaki tek karakterli tuşları
              preventDefault ile engeller (harf/sembol hiç yazılmaz).
              Ctrl/Meta kombinasyonları (kopyala/yapıştır/tümünü seç)
              serbest bırakılır. Backspace/Delete bir noktanın üstüne denk
              geldiğinde noktayla birlikte bitişik haneyi de siler — aksi
              halde 'input' handler'ı sildiği noktayı aynı basamak sayısından
              anında geri koyar ve kullanıcı hiçbir şey silememiş gibi
              görünür.
           2) input: her değişiklikte (yazma/silme/yapıştırma fark etmeksizin)
              maskReformat basamakları çıkarıp dd.aa.yyyy kalıbında yeniden
              dizer — 2./4. haneden sonra nokta otomatik gelir, max 8 hane. */
        fp.altInput.addEventListener('keydown', function (e) {
            var key = e.key || '';
            if (e.ctrlKey || e.metaKey) return; /* kopyala/yapıştır/tümünü seç kısayolları serbest */
            if (key === 'Backspace') {
                var pos = fp.altInput.selectionStart, posEnd = fp.altInput.selectionEnd;
                if (pos === posEnd && pos > 0 && fp.altInput.value.charAt(pos - 1) === '.') {
                    e.preventDefault();
                    var v = fp.altInput.value;
                    fp.altInput.value = v.slice(0, pos - 2) + v.slice(pos);
                    fp.altInput.setSelectionRange(pos - 2, pos - 2);
                    dispatchNative(fp.altInput, 'input');
                }
                return;
            }
            if (key === 'Delete') {
                var dp = fp.altInput.selectionStart, dpEnd = fp.altInput.selectionEnd;
                if (dp === dpEnd && fp.altInput.value.charAt(dp) === '.') {
                    e.preventDefault();
                    var v2 = fp.altInput.value;
                    fp.altInput.value = v2.slice(0, dp) + v2.slice(dp + 2);
                    fp.altInput.setSelectionRange(dp, dp);
                    dispatchNative(fp.altInput, 'input');
                }
                return;
            }
            var navKeys = ['Tab', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End',
                'Enter', 'Escape', 'F4', 'Shift', 'Control', 'Alt', 'Meta', 'CapsLock'];
            if (navKeys.indexOf(key) !== -1) return;
            if (key.length === 1 && !/[0-9.]/.test(key)) {
                e.preventDefault(); /* rakam/nokta dışı karakter kabul edilmez */
            }
        });

        /* ── Elle yazım güvenliği ────────────────────────────────────────
           flatpickr'ın kendi blur/Enter işleyicisi (allowInput) parse
           başarısız olduğunda alanı SESSİZCE boşaltır (hem görünen hem
           gizli input, doğrulandı: flatpickr.min.js setDate → 0 seçili
           tarih → clear()). Bu, kullanıcının önceki geçerli tarihini
           kaybettirir. Ayrıca flatpickr'ın parse'ı taşan gün/ay değerlerini
           (32.13.2026 gibi) JS Date aritmetiğiyle SESSİZCE başka bir geçerli
           tarihe yuvarlayabilir — bu yüzden flatpickr'ın "başarılı" sonucuna
           tek başına güvenilmez, aşağıdaki 'blur' dinleyicisi kendi katı
           takvim doğrulamasını (parseStrictDMY) yapar. flatpickr'ın KENDİ
           blur dinleyicisinden SONRA çalışır (aynı elemana sonradan eklenen
           dinleyiciler DOM ekleme sırasına göre tetiklenir) ve üç durumu
           ayırt eder:
             1) Kullanıcı hiçbir şey yazmadı (odaklanıp çıktı)      → no-op
             2) Kullanıcı alanı BİLEREK boşalttı                    → kabul et + event yay
             3) Kullanıcı tam olarak gg.aa.yyyy kalıbında VE takvimde
                var olan bir tarih yazdı                            → kabul et
                (flatpickr durumu bu ISO ile kesinleştirilir, kendi
                parse'ına güvenilmez)
             4) Kullanıcı geçersiz/eksik/taşan bir şey yazdı         → son
                geçerli değere geri dön + kısa görsel uyarı (alan yanlış
                değerde sessizce kalmaz). Enter tuşu da flatpickr içinde
                senkron blur() çağırdığı için aynı yoldan geçer, ayrı
                işleyici gerekmez. */
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
            maskReformat(fp.altInput);
            lastTypedRaw = fp.altInput.value;
        });
        fp.altInput.addEventListener('blur', function () {
            if (!typedSinceFocus) return;
            typedSinceFocus = false;
            var raw = lastTypedRaw.trim();
            if (raw === '') {
                /* Bilinçli boşaltma — yeni durumu kabul et. */
                var clearedIso = input.value || '';
                if (clearedIso !== lastValidIso) {
                    dispatchNative(input, 'input');
                    dispatchNative(input, 'change');
                }
                lastValidIso = clearedIso;
                return;
            }
            var strictIso = parseStrictDMY(raw);
            if (strictIso) {
                /* Takvimde gerçekten var olan bir tarih — flatpickr durumunu
                   bu ISO ile kesinleştir (kendi parse'ının rolled-over
                   sonucuna güvenme). */
                try { fp.setDate(strictIso, false); } catch (e) { /* yoksay */ }
                var confirmedIso = input.value || strictIso;
                if (confirmedIso !== lastValidIso) {
                    dispatchNative(input, 'input');
                    dispatchNative(input, 'change');
                }
                lastValidIso = confirmedIso;
                return;
            }
            /* raw doluydu ama gg.aa.yyyy kalıbında/takvimde geçerli bir
               tarih değildi (eksik hane, taşan gün/ay) — geri al. */
            if (lastValidIso) {
                try { fp.setDate(lastValidIso, false); } catch (e) { /* yoksay */ }
            } else {
                try { fp.clear(false); } catch (e) { /* yoksay */ }
                fp.altInput.value = '';
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
