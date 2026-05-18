namespace CalibraHub.Application.Contracts.Common;

/// <summary>
/// API endpoint'lerinin standart JSON cevap formati (rapor §2.7 oneri).
///
/// Mevcut 796 manuel "Json(new { success, ... })" cagrisinin yerine kullanim — yeni
/// kod bu format'i kullanir; eski kod bozulmaz, kademeli migrate edilir.
///
/// Su anki anonymous donus formatlari kullanim:
///   return Json(new { success = true, data = ... })
///   return Json(new { success = false, error = "..." })
///
/// Yeni standart:
///   return Ok(ApiResponse&lt;XxxDto&gt;.Successful(data));
///   return BadRequest(ApiResponse.Failed("hata mesaji"));
///   // Ya da exception firlat — ApiExceptionMiddleware otomatik wrap eder.
/// </summary>
public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error = null,
    IReadOnlyList<string>? Warnings = null)
{
    public static ApiResponse<T> Successful(T data, IReadOnlyList<string>? warnings = null)
        => new(true, data, null, warnings);

    public static ApiResponse<T> Failed(string message, string? traceId = null, string? detail = null)
        => new(false, default, new ApiError(message, traceId, detail));

    public static ApiResponse<T> Failed(ApiError error)
        => new(false, default, error);
}

/// <summary>
/// Tip parametresi olmayan basit hata cevabi (data alanina ihtiyac olmadiginda).
/// Kullanim: return ApiResponse.Failed("kayit silinemedi", traceId);
/// </summary>
public static class ApiResponse
{
    public static ApiResponse<object?> Successful() => new(true, null);

    public static ApiResponse<object?> Failed(string message, string? traceId = null, string? detail = null)
        => new(false, null, new ApiError(message, traceId, detail));

    public static ApiResponse<object?> Failed(ApiError error)
        => new(false, null, error);
}

/// <summary>
/// Hata detayi — kullaniciya gosterilir + log icin traceId tasinir.
/// </summary>
/// <param name="Message">Kullanici dostu hata mesaji (Turkce).</param>
/// <param name="TraceId">Logla eslesen iz id (telemetry).</param>
/// <param name="Detail">Opsiyonel teknik detay (development modu yalniz).</param>
/// <param name="Errors">Field bazli validation hatalari (key=field, value=[messages]).</param>
public sealed record ApiError(
    string Message,
    string? TraceId = null,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
