/**
 * showConfirm — Global onay modali (rapor §2.1 cozumu).
 *
 * 5 ayri .cshtml'de inline tanimli olan showConfirm fonksiyonlarini tek noktaya
 * konsolide eder. CalibraAlert.confirm ustune ince bir wrapper — opts API'sini
 * normalize eder ve geriye uyum saglar.
 *
 * Eski kullanim (5 farkli imza vardi):
 *   showConfirm({ title, message, okLabel, danger })  → PriceList/Report, RoutingEdit, PriceGroupEdit
 *   showConfirm(repName)                              → SalesRepEdit (otomatik mesaj olusturur)
 *   showConfirm(name)                                 → CariGroupEdit (ayni)
 *
 * Yeni standart imza (geri-uyumlu):
 *   showConfirm({ title?, message, okLabel?, cancelLabel?, danger? }): Promise<boolean>
 *   showConfirm("sicil adi"): Promise<boolean>   ← legacy: "X silinecek. Devam edilsin mi?" mesaji
 *
 * Cikti: Promise<boolean> — true = OK tiklandi, false = Vazgec/Esc/backdrop tiklandi
 *
 * Bu dosya _Layout.cshtml'de bir kere yuklenir ve `window.showConfirm` olarak global olur.
 * CalibraAlert.confirm zaten /js/calibraAlert.js'de var — bu sadece API normalizasyonu.
 */
(function () {
    'use strict';

    if (typeof window.showConfirm === 'function') {
        // Zaten yuklenmis (duplicate script tag olabilir) — atla
        return;
    }

    function showConfirm(arg) {
        // Eski API: showConfirm("AdSoyad") → otomatik mesaj
        if (typeof arg === 'string') {
            arg = {
                title: 'Silme Onayi',
                message: '"' + arg + '" silinsin mi?',
                okLabel: 'Evet, Sil',
                cancelLabel: 'Vazgec',
                danger: true
            };
        }

        var opts = arg || {};
        var message = opts.message || 'Devam edilsin mi?';

        // CalibraAlert.confirm yuklenmemisse native fallback
        if (!window.CalibraAlert || typeof window.CalibraAlert.confirm !== 'function') {
            return Promise.resolve(window.confirm(message));
        }

        return window.CalibraAlert.confirm(message, {
            title:       opts.title       || 'Onay',
            okText:      opts.okLabel     || opts.okText     || 'Tamam',
            cancelText:  opts.cancelLabel || opts.cancelText || 'Vazgec',
            danger:      opts.danger === true
        });
    }

    window.showConfirm = showConfirm;
})();
