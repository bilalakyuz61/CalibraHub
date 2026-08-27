package com.calibrahub.mobile.data

import kotlin.coroutines.cancellation.CancellationException

/**
 * `runCatching`'in coroutine-guvenli hali: [CancellationException] YAKALANMAZ, yeniden atilir.
 *
 * NEDEN: `runCatching` `Throwable` yakalar, dolayisiyla coroutine IPTALINI de yakalar ve
 * `Result.failure` uretir. Sonuc: iptal, cagiran katmanda "hata" gibi gorunur ve iptal
 * mesaji KULLANICIYA gosterilir. Sahada goruldugu hali:
 *
 *   Stok Sorgu ekraninda arama yapilirken ekrana
 *   "The coroutine scope left the composition" yaziyordu.
 *
 * O metin `rememberCoroutineScope()` scope'u kompozisyondan cikinca firlatilan
 * `LeftCompositionCancellationException`'in mesaji. Ayni sinif hata LaunchedEffect(query)
 * her tusa basista yeniden baslarken de olusur (onceki tur iptal edilir).
 *
 * Iptal bir HATA DEGILDIR: ya kullanici yazmaya devam etmistir ya da ekran kapanmistir.
 * Ikisinde de gosterilecek bir sey yoktur — istisna yukari birakilir, coroutine sessizce olur.
 *
 * KURAL: `data` katmanindaki suspend cagrilarda `runCatching` DEGIL bu kullanilir.
 */
internal inline fun <T> runCatchingApi(block: () -> T): Result<T> =
    try {
        Result.success(block())
    } catch (cancellation: CancellationException) {
        throw cancellation
    } catch (t: Throwable) {
        Result.failure(t)
    }
