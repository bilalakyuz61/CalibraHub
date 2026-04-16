using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;

namespace CalibraHub.Web.Infrastructure.Workspace;

public sealed class WorkspaceRedirectPreservationMiddleware
{
    private const string WorkspaceKey = "workspace";
    private const string WorkspaceValue = "1";

    private readonly RequestDelegate _next;

    public WorkspaceRedirectPreservationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (!IsWorkspaceFrameRequest(context.Request) ||
            !IsRedirectStatusCode(context.Response.StatusCode) ||
            !context.Response.Headers.TryGetValue("Location", out var locationValues))
        {
            return;
        }

        var rawLocation = locationValues.ToString();
        if (string.IsNullOrWhiteSpace(rawLocation))
        {
            return;
        }

        var updatedLocation = PreserveWorkspaceParameter(context, rawLocation);
        if (string.IsNullOrWhiteSpace(updatedLocation) ||
            string.Equals(updatedLocation, rawLocation, StringComparison.Ordinal))
        {
            return;
        }

        context.Response.Headers.Location = updatedLocation;
    }

    private static string PreserveWorkspaceParameter(HttpContext context, string rawLocation)
    {
        if (rawLocation.StartsWith('#'))
        {
            return rawLocation;
        }

        if (rawLocation.StartsWith("//", StringComparison.Ordinal))
        {
            return rawLocation;
        }

        if (Uri.TryCreate(rawLocation, UriKind.Absolute, out var absoluteUri))
        {
            return IsSameOrigin(context.Request, absoluteUri)
                ? AppendWorkspaceParameter(absoluteUri, absolute: true)
                : rawLocation;
        }

        var requestUri = new Uri($"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}");
        if (!Uri.TryCreate(requestUri, rawLocation, out var resolvedUri))
        {
            return rawLocation;
        }

        return AppendWorkspaceParameter(resolvedUri, absolute: false);
    }

    private static string AppendWorkspaceParameter(Uri uri, bool absolute)
    {
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (query.TryGetValue(WorkspaceKey, out var existingWorkspace) &&
            existingWorkspace.Any(value => string.Equals(value, WorkspaceValue, StringComparison.Ordinal)))
        {
            return absolute
                ? uri.ToString()
                : $"{uri.AbsolutePath}{uri.Query}{uri.Fragment}";
        }

        var builder = new QueryBuilder();
        foreach (var entry in query)
        {
            if (string.Equals(entry.Key, WorkspaceKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in entry.Value)
            {
                builder.Add(entry.Key, value ?? string.Empty);
            }
        }

        builder.Add(WorkspaceKey, WorkspaceValue);

        var queryString = builder.ToQueryString().Value;
        return absolute
            ? $"{uri.GetLeftPart(UriPartial.Path)}{queryString}{uri.Fragment}"
            : $"{uri.AbsolutePath}{queryString}{uri.Fragment}";
    }

    private static bool IsWorkspaceFrameRequest(HttpRequest request)
    {
        // ?workspace=1 query flag'i VEYA Sec-Fetch-Dest: iframe header'i
        // (iframe icindeki link click'lerde query flag'i kaybolur ama
        // browser Sec-Fetch-Dest'i her zaman dogru gonderir).
        return string.Equals(request.Query[WorkspaceKey], WorkspaceValue, StringComparison.Ordinal)
            || string.Equals(request.Headers["Sec-Fetch-Dest"], "iframe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrigin(HttpRequest request, Uri uri)
    {
        var requestPort = request.Host.Port ?? (string.Equals(request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        var uriPort = uri.IsDefaultPort
            ? (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : uri.Port;

        return (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
               string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
               uriPort == requestPort;
    }

    private static bool IsRedirectStatusCode(int statusCode)
    {
        return statusCode is StatusCodes.Status301MovedPermanently
            or StatusCodes.Status302Found
            or StatusCodes.Status303SeeOther
            or StatusCodes.Status307TemporaryRedirect
            or StatusCodes.Status308PermanentRedirect;
    }
}
