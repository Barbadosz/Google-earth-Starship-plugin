using StarshipMissionTracker.Api.Http;

namespace StarshipMissionTracker.Api.Endpoints;

/// <summary>
/// GET <c>/</c> and <c>/control</c> -> 302 to <c>/control/index.html</c>. The page itself (and its .js/.css) is served
/// by the default static file middleware from <c>wwwroot/control/</c> (wired in Program.cs).
/// </summary>
public static class ControlPageEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/", Redirect);
        app.MapGet(ServerUrls.ControlPath, Redirect);
        return app;
    }

    private static IResult Redirect(HttpRequest request) =>
        Results.Redirect(request.PathBase + ServerUrls.ControlPagePath, permanent: false);
}
