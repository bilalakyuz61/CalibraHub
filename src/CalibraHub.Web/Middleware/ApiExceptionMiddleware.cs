using System.Diagnostics;
using System.Text.Json;
using CalibraHub.Application.Contracts.Common;
using CalibraHub.Domain.Common;

namespace CalibraHub.Web.Middleware;

/// <summary>
/// JSON API endpoint'leri icin global exception handler (rapor §2.7).
///
/// HTML/Razor view'lar zaten UseExceptionHandler("/Home/Error") tarafindan
/// yakalanir; bu middleware AYRICA JSON path'ler icin standart ApiResponse<T>
/// formatinda hata cevabi doner. Davranis:
///
///   DomainException     -> HTTP 400 + ApiError(message)        (kullanici invariant ihlali)
///   ValidationException -> HTTP 400 + ApiError + Errors dict   (form validation)
///   NotFoundException   -> HTTP 404 + ApiError(message)
///   Generic Exception   -> HTTP 500 + ApiError(generic msg)    (detay log'a, asla cliente cikmaz)
///
/// JSON path tespiti: Request.Path /api/ ile baslar VEYA Accept: application/json header'i var.
///
/// Kullanim: app.UseApiExceptionHandler() — UseRouting'ten ONCE eklenmeli ki tum
/// downstream exception'lari yakalasin.
/// </summary>
public sealed class ApiExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _log;
    private readonly IHostEnvironment _env;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> log,
        IHostEnvironment env)
    {
        _next = next;
        _log = log;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // JSON expected mi? (api path veya Accept header)
            if (!IsApiRequest(context))
            {
                // HTML akisi — UseExceptionHandler'a fallback (re-throw)
                throw;
            }

            // Response zaten yazilmaya basladiysa middleware'in body yazma sansi yok
            if (context.Response.HasStarted)
            {
                _log.LogError(ex, "[ApiException] Exception sonrasi response zaten yazilmis — middleware bypass");
                throw;
            }

            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        int statusCode;
        ApiError error;

        switch (ex)
        {
            case ValidationException vex:
                statusCode = StatusCodes.Status400BadRequest;
                error = new ApiError(vex.Message, traceId, Errors: vex.Errors);
                _log.LogInformation("[ApiException] Validation: {Msg} (traceId={TraceId})", vex.Message, traceId);
                break;

            case DomainException dex:
                statusCode = StatusCodes.Status400BadRequest;
                error = new ApiError(dex.Message, traceId);
                _log.LogInformation("[ApiException] Domain: {Msg} (traceId={TraceId})", dex.Message, traceId);
                break;

            case NotFoundException nex:
                statusCode = StatusCodes.Status404NotFound;
                error = new ApiError(nex.Message, traceId);
                _log.LogInformation("[ApiException] NotFound: {Msg} (traceId={TraceId})", nex.Message, traceId);
                break;

            case UnauthorizedAccessException uex:
                statusCode = StatusCodes.Status401Unauthorized;
                error = new ApiError("Yetkisiz erisim.", traceId);
                _log.LogWarning("[ApiException] Unauthorized: {Msg} (traceId={TraceId})", uex.Message, traceId);
                break;

            default:
                statusCode = StatusCodes.Status500InternalServerError;
                var detail = _env.IsDevelopment() ? ex.ToString() : null;
                error = new ApiError("Sunucu hatasi olustu. Tekrar deneyin.", traceId, detail);
                _log.LogError(ex, "[ApiException] Unhandled (traceId={TraceId})", traceId);
                break;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiResponse.Failed(error);
        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOpts, context.RequestAborted);
    }

    /// <summary>
    /// API request mi? Path /api/ ile baslar, /Integrations/api ile baslar VEYA
    /// Accept header'i application/json ise. HTML form post'lari otomatik bypass.
    /// </summary>
    private static bool IsApiRequest(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Contains("/api/", StringComparison.OrdinalIgnoreCase)) return true;   // /Integrations/api/...
        var accept = context.Request.Headers.Accept.ToString();
        if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// <summary>
/// Pipeline'a ekleme kolaylik metodu: app.UseApiExceptionHandler();
/// </summary>
public static class ApiExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseApiExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<ApiExceptionMiddleware>();
}
