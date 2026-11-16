using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Api.Services;

namespace StarshipMissionTracker.Api.Endpoints;

/// <summary>
/// GET <c>/kml/master|planned|history|live?mission=&lt;code&gt;</c> (mission optional = current mission). Extra query
/// parameters (the NetworkLinkControl cookie <c>r=&lt;n&gt;</c> Google Earth appends) are ignored. Errors are plain text
/// (Google Earth shows them as the link error). Every response has the no-cache headers.
/// </summary>
public static class KmlEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(ServerUrls.MasterPath, (HttpContext context, string? mission, KmlDocumentService service, IOptions<HostFilterOptions> hosts) =>
            Handle(context, KmlDocumentKind.Master, mission, service, hosts.Value));
        app.MapGet(ServerUrls.PlannedPath, (HttpContext context, string? mission, KmlDocumentService service, IOptions<HostFilterOptions> hosts) =>
            Handle(context, KmlDocumentKind.Planned, mission, service, hosts.Value));
        app.MapGet(ServerUrls.HistoryPath, (HttpContext context, string? mission, KmlDocumentService service, IOptions<HostFilterOptions> hosts) =>
            Handle(context, KmlDocumentKind.History, mission, service, hosts.Value));
        app.MapGet(ServerUrls.LivePath, (HttpContext context, string? mission, KmlDocumentService service, IOptions<HostFilterOptions> hosts) =>
            Handle(context, KmlDocumentKind.Live, mission, service, hosts.Value));
        return app;
    }

    private static async Task<IResult> Handle(
        HttpContext context, KmlDocumentKind kind, string? mission, KmlDocumentService service, HostFilterOptions hosts)
    {
        var code = string.IsNullOrEmpty(mission) ? null : mission;
        if (code is not null && !InputRules.IsValidMissionCode(code))
        {
            return KmlResults.PlainError(StatusCodes.Status400BadRequest, InputRules.MissionCodeRule);
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KmlEndpoints));
        var urls = ServerUrls.FromRequest(context.Request, hosts.AllowedHostNames, out var usedFallback);
        if (usedFallback)
        {
            logger.LogWarning(
                "KML {Kind} request with Host {Host} that is not a local name: URLs use {BaseUrl} instead",
                kind, LocalHostRule.ForLog(context.Request.Host), urls.BaseUrl);
        }

        var document = await service.BuildAsync(kind, code, urls, context.RequestAborted).ConfigureAwait(false);
        if (document is null)
        {
            logger.LogWarning("KML {Kind} request: mission {MissionCode} not found", kind, code ?? "(current)");
            return KmlResults.PlainError(
                StatusCodes.Status404NotFound,
                code is null ? "No mission in the database." : $"Unknown mission '{code}'.");
        }

        return KmlResults.Kml(document.Xml);
    }
}
