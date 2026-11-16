using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace StarshipMissionTracker.Api.Http;

/// <summary>
/// gzip for the KML (and JSON) responses (N-20; Google Earth accepts gzip). Only the service registration lives here;
/// the wiring package calls <c>app.UseResponseCompression()</c> before the endpoints.
/// </summary>
public static class KmlCompression
{
    public static IServiceCollection AddKmlResponseCompression(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddResponseCompression(options =>
        {
            // Plain HTTP on loopback only; no secrets in the responses, so BREACH is not a concern here.
            options.EnableForHttps = false;
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat([KmlResults.MediaType]).ToArray();
        });
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        return services;
    }
}
