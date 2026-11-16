using Microsoft.Net.Http.Headers;

namespace StarshipMissionTracker.Api.Http;

/// <summary>
/// Requires <c>Content-Type: application/json</c> (a charset parameter is allowed) on mutating endpoints, else 415.
/// A browser sends this type cross-origin only after a CORS preflight, which this server never grants (no CORS headers),
/// so this stops plain cross-site form/fetch requests (CSRF). It does not help against DNS rebinding (the hostile page
/// is then same-origin, no preflight is sent); that case is covered by the Host check of <see cref="LoopbackOnlyFilter"/>.
/// </summary>
public sealed class JsonContentTypeFilter(ILogger<JsonContentTypeFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!IsJson(request.ContentType))
        {
            logger.LogWarning(
                "Rejected {Method} {Path}: content type {ContentType} is not application/json", request.Method, request.Path, request.ContentType ?? "(none)");
            return ApiErrors.Result(
                StatusCodes.Status415UnsupportedMediaType, "unsupported-media-type", "Content-Type must be application/json.");
        }

        return await next(context).ConfigureAwait(false);
    }

    public static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        && (!parsed.Charset.HasValue || parsed.Charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase));
}
