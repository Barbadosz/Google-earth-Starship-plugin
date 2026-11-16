namespace StarshipMissionTracker.Api.Endpoints;

/// <summary>Maps every Api endpoint group (one call for Program.cs).</summary>
public static class StarshipEndpoints
{
    public static IEndpointRouteBuilder MapStarshipEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        KmlEndpoints.Map(app);
        MissionEndpoints.Map(app);
        SimulationEndpoints.Map(app);
        ControlPageEndpoints.Map(app);
        return app;
    }
}
