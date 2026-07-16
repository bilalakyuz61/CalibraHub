/* ═══════════════════════════════════════════════════════════════════
   CalibraHub Telefon Alanı Standardı (calibra-phone.js)
   ---------------------------------------------------------------
   Sayfadaki TÜM input[type="tel"] (+ opt-in input[data-phone]) alanlarını
   otomatik olarak canlı TR telefon maskesine ("0 (5XX) XXX XX XX") bağlar.
   Yeni ekranlar hiçbir şey yapmadan standarda dahil olur; dinamik eklenen
   inputlar MutationObserver ile yakalanır. İskelet: calibra-datepicker.js
   (bkz. wwwroot/js/calibra-datepicker.js) ile birebir aynı desen
   (guard + enhance + scan + MutationObserver + value-interceptor).

   Format mantığı (eski Views/CompanyUser/Index.cshtml formatPhoneTr'den
   devralındı, program geneline taşındı):
     • +90 / 90 uluslararası öneki ve baştaki '0'lar düşürülür → 10 haneli
       yerel numara → '0 (XXX) XXX XX XX' canlı maske (yazarken formatlanır).
     • Sabit hat da (0212...) aynı gruplamayla formatlanır — mobil/sabit
       ayrımı yok, tutarlı tek kalıp.
     • FARK (eski CompanyUser davranışından bilinçli sapma): 10 haneden
       farklı / TR-dışı değerler (prefix+sıfır düşürüldükten sonra 10
       haneye inmeyen her şey — yabancı numara, dahili/extension eklenmiş
       numara, hatalı yapıştırma) artık zorla kırpılmıyor; OLDUĞU GİBİ
       bırakılır (bozma). Eski kod `d.substring(0,10)` ile fazlalığı
       sessizce siliyordu — global enhancer'da bu veri kaybı riskini
       almıyoruz.

   Davranış sözleşmesi (mevcut ekran kodları kırılmaz):
     • Orijinal input DOM'da kalır, name/id aynen çalışır — form post
       formatlanmış (maskeli) string'i gönderir; backend NormalizePhone
       rakamlara indirger (var olan sözleşme, burada değişmedi).
     • Programatik `el.value = '5321234567'` atamaları da otomatik
       formatlanır (per-element value interceptor — datepicker'daki
       desenle birebir aynı teknik, HTMLInputElement.prototype.value
       descriptor'ı instance üzerinde override edilir).
     • Mount anındaki mevcut değer (server-render / DB'den gelen ham
       değer) de bir kez formatlanır — sayfa ilk açıldığında da maske
       standardı uygulanır, sadece kullanıcı yazarken değil.

   Kapsam dışı (bilinçli):
     • React'in yönettiği inputlar (fiber key'li) dokunulmaz — React'in
       controlled-input değer takibi ile çakışmamak için native kalırlar.
     • data-native-phone attribute'u ile ekran bazlı opt-out yapılabilir
       (örn. WhatsApp ham uluslararası numara / pairing alanları —
       "905XXXXXXXXX" gibi ayraçsız formatların bozulmaması gereken yerler).
   ═══════════════════════════════════════════════════════════════════ */
(function () {
    'use strict';
    if (window.CalibraPhone) return;

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

    // ── Format çekirdeği ────────────────────────────────────────────
    // Hem canlı maskeleme (input event) hem programatik atama (value
    // interceptor) hem de dışa açık window.CalibraPhone.format() bunu kullanır.
    function formatPhoneTr(raw) {
        if (raw == null) return '';
        var s = String(raw);
        var d = s.replace(/\D+/g, '');
        if (!d) return s; // rakam yok — dokunma (bos veya salt metin/etiket)

        var work = d;
        // Paste edildiyse '+90 ...' veya '90...' baslangic kodunu dusur
        if (work.length >= 12 && work.indexOf('90') === 0) work = work.substring(2);
        // Bastaki '0'lari dusur — AMA en az 1 hane kalacak sekilde
        while (work.length > 1 && work.charAt(0) === '0') work = work.substring(1);
        // Sadece '0' yazildi → ham goster, format baslama
        if (work === '0') return '0';

        // 10 haneyi asiyorsa TR-disi/gecersiz kabul edilir — kirpmadan
        // ham degeri (orijinal string) oldugu gibi birak (veri kaybi yok).
        if (work.length > 10) return s;
        if (work.length === 0) return '';

        var out = '0 (' + work.substring(0, Math.min(3, work.length));
        if (work.length <= 3) return out;
        out += ') ' + work.substring(3, Math.min(6, work.length));
        if (work.length <= 6) return out;
        out += ' ' + work.substring(6, Math.min(8, work.length));
        if (work.length <= 8) return out;
        out += ' ' + work.substring(8, 10);
        return out;
    }

    function enhance(input) {
        if (!input || input.nodeType !== 1 || input.tagName !== 'INPUT') return;
        if (input.type !== 'tel' && !input.hasAttribute('data-phone')) return;
        if (input._calibraPhoneDone) return;                 // zaten baglandi
        if (input.hasAttribute('data-native-phone')) return; // ekran bazli opt-out
        if (isReactManaged(input)) return;                   // React controlled input — dokunma

        input._calibraPhoneDone = true;

        input.addEventListener('input', function () {
            var formatted = formatPhoneTr(this.value);
            if (formatted !== this.value) {
                this.value = formatted;
                try { this.setSelectionRange(formatted.length, formatted.length); } catch (e) { /* yoksay */ }
            }
        });

        // Programatik `el.value = ...` atamalarini otomatik formatlar
        // (datepicker'daki value-interceptor deseniyle ayni teknik).
        if (nativeValueDesc && nativeValueDesc.set && nativeValueDesc.get) {
            try {
                Object.defineProperty(input, 'value', {
                    configurable: true,
                    get: function () { return nativeValueDesc.get.call(input); },
                    set: function (v) {
                        nativeValueDesc.set.call(input, formatPhoneTr(v == null ? '' : v));
                    }
                });
            } catch (e) { /* defineProperty engellendiyse interceptor atlanir */ }
        }

        // Mount anindaki mevcut deger (server-render / DB'den gelen ham deger)
        // de bir kez formatlanir — sayfa ilk acildiginda da maske standardi uygulanir.
        var current = input.value;
        var formattedInitial = formatPhoneTr(current);
        if (formattedInitial !== current) input.value = formattedInitial;
    }

    function scan(root) {
        if (!root) return;
        if (root.nodeType === 1) {
            if (root.matches && root.matches('input[type="tel"], input[data-phone]')) enhance(root);
            if (root.querySelectorAll) {
                var list = root.querySelectorAll('input[type="tel"], input[data-phone]');
                for (var i = 0; i < list.length; i++) enhance(list[i]);
            }
        } else if (root === document) {
            var all = document.querySelectorAll('input[type="tel"], input[data-phone]');
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

    window.CalibraPhone = {
        enhance: enhance,
        scan: scan,
        format: formatPhoneTr
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
