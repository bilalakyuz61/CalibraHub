package com.calibrahub.mobile.ui.warehouse

/**
 * Depo ekranları ortak sayı/tarih format yardımcıları.
 *
 * CalibraHubAndroid'de her ekran dosyası (StockDocScreen/TransferScreen/CountScreen) kendi
 * `private fun formatQty` kopyasını taşırdı — bir `internal`/public tek kopya paylaşılamıyordu
 * çünkü aynı paketteki `private` fonksiyonlarla JVM-düzeyinde "Conflicting overloads" hatası
 * veriyordu (bkz. Android `DeliveryScreen.formatDeliveryQty` üstü KDoc). KMP portunda BİLİNÇLİ
 * olarak TEK ortak fonksiyona konsolide edildi (CLAUDE.md "Tekrarlanan mantığı tek kaynağa çek") —
 * o çakışma yalnızca Android/JVM derleyicisinin private-internal ayrımından kaynaklanıyordu, tek
 * bir `internal` bildirim varken hiç oluşmaz.
 *
 * `"%.2f".format(...)` (kotlin.text.format, java.util.Formatter tabanlı) commonMain'de YOKTUR
 * (JVM'e özgü) — bu yüzden yuvarlama/biçimlendirme manuel yapılır (kotlin.math.round + tam sayı
 * bölme), herhangi bir platform API'sine bağımlılık olmadan.
 */

/** Tam sayıları ".00" olmadan, ondalıklıları 2 haneye yuvarlayarak gösterir (Android ekranlarının
 * V1 formatıyla AYNI çıktı). */
internal fun formatQty(q: Double): String {
    val rounded = kotlin.math.round(q * 100) / 100.0
    val whole = rounded.toLong()
    if (rounded == whole.toDouble()) return whole.toString()
    val negative = rounded < 0
    val absRounded = kotlin.math.abs(rounded)
    val absWhole = absRounded.toLong()
    val fracHundredths = kotlin.math.round((absRounded - absWhole) * 100).toLong()
    val fracStr = fracHundredths.toString().padStart(2, '0')
    return (if (negative) "-" else "") + "$absWhole.$fracStr"
}

/**
 * ISO tarih/datetime string'inin ilk 10 karakterini gün.ay.yıl'a çevirir; parse edilemezse ham
 * metni döner (CalibraHubAndroid `formatIsoDate` ile AYNI sözleşme/çıktı). `java.time.LocalDate`
 * (JVM'e özgü) YERİNE manuel sayısal ayrıştırma + aralık doğrulama (yıl/ay/gün) kullanılır — bu
 * fonksiyon salt GÖSTERİM amaçlı olduğundan artık-yıl/ay-uzunluğu gibi tam takvim doğrulaması
 * BİLİNÇLİ olarak atlanır (yalnız ay 1-12, gün 1-31 aralık kontrolü); sunucudan gelen değer zaten
 * geçerli bir tarihtir, bu kontrol yalnız çöp/boş veriye karşı savunmadır.
 */
internal fun formatIsoDate(raw: String): String {
    val datePart = if (raw.length >= 10) raw.substring(0, 10) else raw
    val parts = datePart.split("-")
    if (parts.size != 3) return raw
    val y = parts[0].toIntOrNull() ?: return raw
    val m = parts[1].toIntOrNull() ?: return raw
    val d = parts[2].toIntOrNull() ?: return raw
    if (m !in 1..12 || d !in 1..31) return raw
    return "${d.toString().padStart(2, '0')}.${m.toString().padStart(2, '0')}.${y.toString().padStart(4, '0')}"
}
