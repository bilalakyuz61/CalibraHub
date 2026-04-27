/*
 * CalibraDirty — iframe icinde calisan kucuk "kirli kayit" takipcisi.
 *
 * - Parent Shell'e postMessage ile dirty/clean durumunu bildirir (tab dot).
 * - Sayfa-ici sag ust yesil nokta KALDIRILDI — sadece tab dot gosterilir.
 * - Input/textarea/select/contenteditable degisikliklerini yakalayip dirty=true yapar.
 * - Form submit'te dirty=false (post sonrasi sayfa yeniden yuklenir).
 * - fetch() ile yapilan /Logistics/Save*, /Sales/SaveDocument, /Finance/Save* vb.
 *   basarili cagrilarinda otomatik olarak dirty=false yapilir (tab dot da kapanir).
 * - Sayfalar save sonrasi manuel olarak `CalibraDirty.clear()` da cagirabilir.
 */
(function () {
    var _dirty = false;
    var _key = null;
    var _suspend = 0;  // programmatik doldurma icin gecici duraklama

    function post(type, extra) {
        try {
            var payload = { type: type, key: _key, url: location.href };
            if (extra) for (var k in extra) payload[k] = extra[k];
            if (window.parent && window.parent !== window) {
                window.parent.postMessage(payload, '*');
            }
        } catch (e) { /* ignore */ }
    }

    function setDirty(v) {
        v = !!v;
        if (_dirty === v) return;
        _dirty = v;
        post('calibra:dirty', { isDirty: v });
    }

    window.CalibraDirty = {
        set: function (v) { setDirty(v); },
        clear: function () { setDirty(false); },
        isDirty: function () { return _dirty; },
        suspend: function () { _suspend++; },
        resume: function () { _suspend = Math.max(0, _suspend - 1); }
    };

    // Parent'tan tab-key al
    window.addEventListener('message', function (e) {
        var d = e && e.data;
        if (d && d.type === 'calibra:init' && d.key) { _key = d.key; }
    });

    // Oto-dirty detect — input/change
    function matchesInput(el) {
        if (!el || !el.matches) return false;
        try { return el.matches('input, textarea, select, [contenteditable="true"]'); } catch (e) { return false; }
    }
    function onAnyInput(e) {
        if (_suspend > 0) return;
        if (matchesInput(e.target)) setDirty(true);
    }
    document.addEventListener('input',  onAnyInput, true);
    document.addEventListener('change', onAnyInput, true);

    // Form submit'te temizle
    document.addEventListener('submit', function () {
        setTimeout(function () { setDirty(false); }, 50);
    }, true);

    // fetch() ile yapilan Save* cagrilarini dinle — basarili donuste dirty=false.
    // URL'de "/Save" geciyor VE HTTP 2xx VE (JSON ise) success !== false ise temizle.
    if (typeof window.fetch === 'function') {
        var _origFetch = window.fetch.bind(window);
        window.fetch = function (input, init) {
            var url = '';
            try { url = typeof input === 'string' ? input : (input && input.url) || ''; } catch (e) { /* ignore */ }
            var isSave = url && /\/Save/i.test(url);
            var p = _origFetch(input, init);
            if (!isSave) return p;
            return p.then(function (resp) {
                try {
                    if (!resp || !resp.ok) return resp;
                    var ct = resp.headers && resp.headers.get ? resp.headers.get('content-type') : '';
                    if (ct && ct.indexOf('application/json') !== -1) {
                        // JSON body: success alani false degilse temizle
                        var clone = resp.clone();
                        clone.json().then(function (data) {
                            if (!data || data.success !== false) setDirty(false);
                        }).catch(function () { setDirty(false); });
                    } else {
                        // Non-JSON 2xx: basarili sayalim
                        setDirty(false);
                    }
                } catch (e) { /* ignore */ }
                return resp;
            });
        };
    }

    // Sayfa yeni yuklendi — temiz
    setDirty(false);
})();
