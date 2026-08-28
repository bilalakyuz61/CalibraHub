package com.calibrahub.mobile.data

import kotlinx.serialization.Serializable

/**
 * Uretim (Production) modulu sozlesme DTO'lari — CalibraHubAndroid `ProductionApi.kt` ile
 * BIREBIR alan adi/tip/opsiyonellik eslesir (bkz. CLAUDE.md — sessiz kirilma sinifi 1).
 * statusCode kumeleri KILITLI: is emri (planned/released/in_progress/completed/closed/cancelled),
 * operasyon (pending/in_progress/completed/skipped) — mobil taraf bu kumeyi TURETMEZ, backend'in
 * dondurdugu statusLabel/statusCode'u oldugu gibi gosterir.
 */

/** Is emri liste satiri — GET work-orders yaniti. plannedDate planlanan BASLANGIC tarihi. */
@Serializable
data class WorkOrderListItemDto(
    val id: Int,
    val number: String,
    val itemCode: String,
    val itemName: String,
    val quantity: Double,
    val unit: String,
    val statusCode: String,
    val statusLabel: String,
    val plannedDate: String? = null,
)

/** Is emri detayi — GET work-orders/{id} yaniti; operations bossa henuz rota/operasyon tanimlanmamis demektir. */
@Serializable
data class WorkOrderDetailDto(
    val id: Int,
    val number: String,
    val itemCode: String,
    val itemName: String,
    val quantity: Double,
    val unit: String,
    val statusCode: String,
    val statusLabel: String,
    val operations: List<WorkOrderOperationDto> = emptyList(),
)

/**
 * Is emri operasyon satiri. goodQuantity/scrapQuantity burada KUMULATIF toplamdir (operasyonun
 * su ana kadar urettigi); [CompleteOperationRequest]'teki AYNI adli alanlar ise bu OTURUMDA
 * eklenecek DELTA'dir — iki anlam FARKLI (koordinator netlestirmesi). canStart/canComplete
 * backend'in durum makinesine gore hesaplanir, mobil taraf kendi kuralini turetmez.
 */
@Serializable
data class WorkOrderOperationDto(
    val id: Int,
    val seq: Int,
    val name: String,
    val machineName: String,
    val statusCode: String,
    val statusLabel: String,
    val goodQuantity: Double,
    val scrapQuantity: Double,
    val canStart: Boolean,
    val canComplete: Boolean,
)

/** Operator kimlik dogrulama istegi — sicil no (personnelCode) + PIN birlikte zorunlu. */
@Serializable
data class AuthOperatorRequest(
    val personnelCode: String,
    val pin: String,
)

@Serializable
data class AuthOperatorResponse(
    val operatorId: Int = 0,
    val name: String = "",
)

@Serializable
data class StartOperationRequest(
    val operationId: Int,
    val operatorId: Int,
)

/** complete istegi govdesi. goodQuantity/scrapQuantity bu OTURUMDA uretilen EK (delta) miktardir
 * — [WorkOrderOperationDto]'daki ayni adli alanlarin KUMULATIF toplamiyla KARISTIRILMAMALI. */
@Serializable
data class CompleteOperationRequest(
    val operationId: Int,
    val operatorId: Int,
    val goodQuantity: Double,
    val scrapQuantity: Double,
    val note: String? = null,
)

/** start/complete basari govdesi — sozlesme yalniz {ok:true} doner, hata HTTP 400/404 + {error}. */
@Serializable
data class OperationActionResponse(
    val ok: Boolean = true,
)

/**
 * GET /api/mobile/production/me — giris yapmis kullaniciya bagli personel kaydi.
 *
 * [linked] false ise kullanicinin personel kaydi yok (ya da uretim operatoru degil) ->
 * [pinRequired] her zaman true doner, istemci PIN yolunu kullanir (fail-safe).
 */
@Serializable
data class MyOperatorDto(
    val linked: Boolean = false,
    val operatorId: Int? = null,
    val name: String? = null,
    val pinRequired: Boolean = true,
)

// ── Durus / aktivite kaydi ──────────────────────────────────────────────────

/** Bir aktivite tipi (Kurulum, Uretim, Ariza…) ve altindaki tanimli sebepler. */
@Serializable
data class ActivityTypeDto(
    val value: Int,
    val label: String = "",
    val reasons: List<ActivityReasonDto> = emptyList(),
)

@Serializable
data class ActivityReasonDto(
    val id: Int,
    val name: String = "",
    val colorHex: String? = null,
)

/** An aktif aktivite. [endedAt] null oldugu surece devam ediyordur. */
@Serializable
data class ActiveActivityDto(
    val id: Int = 0,
    val activityType: String = "",
    val activityTypeLabel: String = "",
    val activityReasonId: Int? = null,
    val activityReasonName: String? = null,
    val startedAt: String? = null,
    val personnelName: String? = null,
)

@Serializable
data class StartActivityBody(
    val operatorId: Int,
    val activityType: Int,
    val activityReasonId: Int? = null,
    val note: String? = null,
)

@Serializable
data class EndActivityBody(val operatorId: Int, val note: String? = null)
