using System.Text;

namespace StarshipMissionTracker.Api.Http;

/// <summary>
/// KML responses for Google Earth (N-15 layer 1): KML content type and no-cache headers on every response (also on the
/// 404 text), no ETag/Last-Modified. Compression is done by the response compression middleware
/// (<see cref="KmlCompression"/>).
/// </summary>
public static class KmlResults
{
    public const string ContentType = "application/vnd.google-earth.kml+xml; charset=utf-8";

    /// <summary>Media type without parameters (for the compression MIME list).</summary>
    public const string MediaType = "application/vnd.google-earth.kml+xml";

    public const string CacheControlValue = "no-store, no-cache, must-revalidate, max-age=0";

    /// <summary>200 with the KML document (UTF-8 without BOM) and the no-cache headers.</summary>
    public static IResult Kml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return new NoCacheResult(Results.Text(xml, ContentType, new UTF8Encoding(false), StatusCodes.Status200OK));
    }

    /// <summary>Plain-text error for Google Earth (shown as the link error), with the same no-cache headers.</summary>
    public static IResult PlainError(int statusCode, string reason) =>
        new NoCacheResult(Results.Text(reason, "text/plain; charset=utf-8", new UTF8Encoding(false), statusCode));

    /// <summary>Sets the no-cache headers and removes validators.</summary>
    public static void ApplyNoCacheHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.CacheControl = CacheControlValue;
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
        response.Headers.Remove("ETag");
        response.Headers.Remove("Last-Modified");
    }

    private sealed class NoCacheResult(IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ApplyNoCacheHeaders(httpContext.Response);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
