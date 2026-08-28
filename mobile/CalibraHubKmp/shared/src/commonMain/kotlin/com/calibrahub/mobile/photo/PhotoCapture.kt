package com.calibrahub.mobile.photo

import androidx.compose.runtime.Composable

/**
 * Cekilen fotografin ham verisi. [bytes] JPEG icerik, [fileName] sunucuya gonderilecek ad.
 *
 * Neden byte dizisi: yukleme multipart ile yapiliyor ve dosya yolu/URI platforma ozgu.
 * Ortak katman yalnizca "icerik + ad" bilir; dosya sistemi kavrami commonMain'e sizmaz.
 */
data class CapturedPhoto(
    val bytes: ByteArray,
    val fileName: String,
    val contentType: String = "image/jpeg",
) {
    // ByteArray icerdigi icin equals/hashCode elle yazilir (data class referans karsilastirir).
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is CapturedPhoto) return false
        return fileName == other.fileName && contentType == other.contentType && bytes.contentEquals(other.bytes)
    }

    override fun hashCode(): Int {
        var result = bytes.contentHashCode()
        result = 31 * result + fileName.hashCode()
        result = 31 * result + contentType.hashCode()
        return result
    }
}

/**
 * Fotograf cekme SEAM'i — [com.calibrahub.mobile.barcode.BarcodeScanner]'in IKIZI, ayni
 * gerekcelerle ayni sekilde tasarlandi:
 *
 * `expect suspend fun takePhoto()` DEGIL, `expect class` + `@Composable expect fun remember…`
 * cunku Android'de kamera bir `ActivityResultLauncher` gerektirir ve bu launcher'in Activity
 * STARTED olmadan ONCE composition icinde kayitli olmasi ZORUNLUDUR (androidx.activity.result
 * kontrati). Duz bir top-level suspend fun bu on-kaydi saglayamaz.
 *
 * Kullanim: ekran once [rememberPhotoCapture] ile ornegi alir, sonra buton onClick
 * coroutine'inde [PhotoCapture.capture] cagirir.
 *
 * Kullanildigi yerler: kalite muayenesi kanit fotografi (bugun); ileride DOF ve fire sebebi.
 */
expect class PhotoCapture {
    /**
     * Kamerayi acar ve kullanici fotograf cekene/iptal edene kadar suspend olur.
     * Iptal, izin reddi veya kamera yoksa `null` doner — bunlar HATA DEGILDIR,
     * cagiran taraf sessizce devam eder.
     */
    suspend fun capture(): CapturedPhoto?
}

/** [PhotoCapture] ornegini composition'a bagli kurar/hatirlar — bkz. sinif KDoc'u. */
@Composable
expect fun rememberPhotoCapture(): PhotoCapture
