package com.calibrahub.mobile.data

import com.calibrahub.mobile.photo.CapturedPhoto
import com.calibrahub.mobile.session.SessionManager
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.request.forms.MultiPartFormDataContent
import io.ktor.client.request.forms.formData
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.Headers
import io.ktor.http.HttpHeaders
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import kotlinx.serialization.Serializable

/**
 * Kalite Muayene istemcisi — `/api/mobile/quality`.
 *
 * Sunucu tarafi is mantigini IQualityService'e devrediyor; dolayisiyla dogrulama, otomatik
 * sonuc (Verdict) hesaplama ve audit web ekraniyla BIREBIR ayni calisir.
 */
class QualityApi(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val base: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/quality"
        }

    /** Plan yoksa sunucu `null` doner — bu HATA DEGIL, sade kayit yolunu tetikler. */
    suspend fun plan(itemId: Int, type: Int): HttpResponse =
        client.get("$base/plan") {
            parameter("itemId", itemId)
            parameter("type", type)
        }

    suspend fun defectCodes(): HttpResponse = client.get("$base/defect-codes")

    suspend fun detail(documentId: Int): HttpResponse = client.get("$base/$documentId")

    suspend fun save(req: SaveInspectionRequest): HttpResponse =
        client.post(base) {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun complete(documentId: Int, disposition: Int?): HttpResponse =
        client.post("$base/$documentId/complete") {
            contentType(ContentType.Application.Json)
            setBody(CompleteInspectionRequest(disposition))
        }

    suspend fun uploadPhoto(documentId: Int, photo: CapturedPhoto): HttpResponse =
        client.post("$base/$documentId/photos") {
            setBody(
                MultiPartFormDataContent(
                    formData {
                        append(
                            key = "file",
                            value = photo.bytes,
                            headers = Headers.build {
                                append(HttpHeaders.ContentType, photo.contentType)
                                append(HttpHeaders.ContentDisposition, "filename=\"${photo.fileName}\"")
                            },
                        )
                    },
                ),
            )
        }

    suspend fun photos(documentId: Int): HttpResponse = client.get("$base/$documentId/photos")
}

// ── Sozlesme ────────────────────────────────────────────────────────────────

/** Muayene tipi — sunucudaki InspectionType enum'unun byte degerleri. */
object InspectionTypes {
    const val INCOMING = 1      // Mal kabul (gelen kalite)
    const val IN_PROCESS = 2    // Proses / ara kontrol
    const val FINAL = 3         // Final / sevkiyat oncesi
}

/** Muayene sonucu — SUNUCUDA hesaplanir, istemci gondermez, yalniz okur. */
object InspectionVerdicts {
    const val CONFORMING = 1
    const val NON_CONFORMING = 2
    // 3 = Sartli Kabul enum'da var ama sunucu HICBIR YERDE atamiyor (olu deger) — UI'da gosterme.
}

/** Uygunsuzluk karari — kullanici secer, yalniz Verdict uygunsuzken gecerli. */
object InspectionDispositions {
    const val ACCEPT = 1        // Kabul
    const val REJECT = 2        // Ret
    const val REWORK = 3        // Yeniden Islem
    const val DEVIATION = 4     // Sapma Izni
}

/** Olcum satiri sonucu — sayisal satirda sunucu tolerans ile hesaplar, gorsel satirda bu deger kullanilir. */
object LineResults {
    const val NOT_EVALUATED = 0
    const val CONFORMING = 1
    const val NON_CONFORMING = 2
}

@Serializable
data class QualityPlanLineDto(
    val id: Int = 0,
    val characteristicName: String = "",
    val nominal: Double? = null,
    val lowerTol: Double? = null,
    val upperTol: Double? = null,
    val unitId: Int? = null,
    val method: String? = null,
    val gaugeName: String? = null,
    val isNumeric: Boolean = true,
    val orderNo: Int = 0,
)

@Serializable
data class QualityPlanDto(
    val planId: Int,
    val planName: String = "",
    val lines: List<QualityPlanLineDto> = emptyList(),
)

@Serializable
data class DefectCodeDto(
    val id: Int,
    val name: String = "",
    val colorHex: String? = null,
)

@Serializable
data class SaveInspectionLine(
    val planLineId: Int? = null,
    val characteristicName: String,
    val nominal: Double? = null,
    val lowerTol: Double? = null,
    val upperTol: Double? = null,
    val measured: Double? = null,
    val isNumeric: Boolean = true,
    val result: Int = LineResults.NOT_EVALUATED,
    val defectCodeId: Int? = null,
    val orderNo: Int = 0,
    val notes: String? = null,
)

@Serializable
data class SaveInspectionRequest(
    val id: Int = 0,               // 0 = yeni; dolu = mevcut muayenenin DocumentId'si
    val planId: Int? = null,
    val itemId: Int? = null,
    val inspectionType: Int = InspectionTypes.INCOMING,
    val sourceKind: String? = null,
    val sourceId: Int? = null,
    val quantity: Double? = null,
    val notes: String? = null,
    val lines: List<SaveInspectionLine> = emptyList(),
)

@Serializable
data class CompleteInspectionRequest(val disposition: Int? = null)

/** Kaydetme yaniti — [verdict] SUNUCUDA hesaplanmis sonuctur; ekran karar sorup sormayacagini bununla belirler. */
@Serializable
data class SaveInspectionResponse(
    val ok: Boolean = false,
    val documentId: Int = 0,
    val documentNumber: String? = null,
    val verdict: Int? = null,
    val status: Int? = null,
    val error: String? = null,
)

@Serializable
data class InspectionPhotoDto(
    val id: Int,
    val fileName: String = "",
    val contentType: String? = null,
    val fileSize: Long = 0,
)

class QualityRepository(private val session: SessionManager) {

    /** Plan yoksa `null` doner (hata degil) — cagiran sade kayit yoluna gecer. */
    suspend fun plan(itemId: Int, type: Int): Result<QualityPlanDto?> = runCatchingApi {
        val resp = session.qualityApi().plan(itemId, type)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        val text = resp.body<String>().trim()
        if (text.isEmpty() || text == "null") null
        else kotlinx.serialization.json.Json { ignoreUnknownKeys = true }.decodeFromString<QualityPlanDto>(text)
    }

    suspend fun defectCodes(): Result<List<DefectCodeDto>> = runCatchingApi {
        val resp = session.qualityApi().defectCodes()
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<DefectCodeDto>>()
    }

    suspend fun save(req: SaveInspectionRequest): Result<SaveInspectionResponse> = runCatchingApi {
        val resp = session.qualityApi().save(req)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<SaveInspectionResponse>()
    }

    suspend fun complete(documentId: Int, disposition: Int?): Result<Unit> = runCatchingApi {
        val resp = session.qualityApi().complete(documentId, disposition)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        Unit
    }

    /** Foto yukleme muayene kaydini ETKILEMEZ: basarisiz olursa muayene yine gecerlidir. */
    suspend fun uploadPhoto(documentId: Int, photo: CapturedPhoto): Result<Unit> = runCatchingApi {
        val resp = session.qualityApi().uploadPhoto(documentId, photo)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        Unit
    }
}
