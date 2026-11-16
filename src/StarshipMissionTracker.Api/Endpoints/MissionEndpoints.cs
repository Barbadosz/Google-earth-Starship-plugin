using StarshipMissionTracker.Api.Dtos;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Services;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Endpoints;

/// <summary>
/// Mission JSON (plan section 4; mission code instead of the Guid in the URL) and the T0 edit
/// <c>PUT /api/missions/{code}/launch-time</c> (loopback + JSON only; 409 while a simulation of that mission is active).
/// </summary>
public static class MissionEndpoints
{
    public const string BasePath = "/api/missions";

    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(BasePath, async (IMissionRepository missions, CancellationToken ct) =>
            ApiErrors.Ok((await missions.ListAsync(ct).ConfigureAwait(false)).Select(MissionDto.From).ToList()));

        app.MapGet(BasePath + "/current", async (IMissionRepository missions, CancellationToken ct) =>
            await missions.GetCurrentInfoAsync(ct).ConfigureAwait(false) is { } info
                ? ApiErrors.Ok(MissionDto.From(info))
                : ApiErrors.NotFound("No mission in the database."));

        app.MapGet(BasePath + "/{code}", async (string code, IMissionRepository missions, CancellationToken ct) =>
        {
            if (!InputRules.IsValidMissionCode(code))
            {
                return ApiErrors.BadRequest(InputRules.MissionCodeRule);
            }

            return await missions.GetInfoAsync(code, ct).ConfigureAwait(false) is { } info
                ? ApiErrors.Ok(MissionDto.From(info))
                : UnknownMission(code);
        });

        app.MapGet(BasePath + "/{code}/trajectory", async (string code, IMissionRepository missions, CancellationToken ct) =>
            await WithMissionAsync(code, missions, ct, mission => ApiErrors.Ok(mission.Vehicles
                .OrderBy(v => v.Type)
                .SelectMany(v => v.FlightPaths.Select(p => TrajectoryDto.From(v, p, mission.Clock)))
                .ToList())).ConfigureAwait(false));

        app.MapGet(BasePath + "/{code}/events", async (string code, IMissionRepository missions, CancellationToken ct) =>
            await WithMissionAsync(code, missions, ct, mission => ApiErrors.Ok(mission.Config.Events
                .OrderBy(e => e.OffsetSeconds)
                .Select(e => MissionEventDto.From(e, mission.Clock))
                .ToList())).ConfigureAwait(false));

        app.MapGet(BasePath + "/{code}/telemetry", GetTelemetryAsync);

        SimulationEndpoints.Mutating(app.MapPut(BasePath + "/{code}/launch-time", SetLaunchTimeAsync));
        return app;
    }

    /// <summary>Query: <c>vehicle</c> (Starship / SuperHeavy), <c>since</c> (UTC, inclusive), <c>limit</c> (1..10000, default 1000).</summary>
    private static async Task<IResult> GetTelemetryAsync(
        string code, HttpRequest request, IMissionRepository missions, ITelemetryRepository telemetry, CancellationToken ct)
    {
        if (!InputRules.IsValidMissionCode(code))
        {
            return ApiErrors.BadRequest(InputRules.MissionCodeRule);
        }

        var query = request.Query;
        VehicleType? vehicle = null;
        if (query.TryGetValue("vehicle", out var vehicleText))
        {
            if (vehicleText.Count != 1 || !TryParseVehicle(vehicleText[0], out var parsed))
            {
                return ApiErrors.BadRequest("'vehicle' must be Starship or SuperHeavy.");
            }

            vehicle = parsed;
        }

        DateTimeOffset? since = null;
        if (query.TryGetValue("since", out var sinceText))
        {
            if (sinceText.Count != 1 || !LaunchClock.TryParseUtc(sinceText[0], out var parsed))
            {
                return ApiErrors.BadRequest("'since' must be an ISO 8601 UTC time ending with Z, e.g. 2026-12-01T23:30:00Z.");
            }

            since = parsed;
        }

        var limit = InputRules.DefaultSampleLimit;
        if (query.TryGetValue("limit", out var limitText))
        {
            if (limitText.Count != 1
                || !int.TryParse(limitText[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > InputRules.MaxSampleLimit)
            {
                return ApiErrors.BadRequest($"'limit' must be an integer in [1, {InputRules.MaxSampleLimit}].");
            }
        }

        var info = await missions.GetInfoAsync(code, ct).ConfigureAwait(false);
        if (info is null)
        {
            return UnknownMission(code);
        }

        var samples = await telemetry.GetSamplesAsync(code, vehicle, since, limit, ct).ConfigureAwait(false);
        return ApiErrors.Ok(samples.Select(s => TelemetrySampleDto.From(s, info.LaunchTimeUtc)).ToList());
    }

    /// <summary>
    /// Body <c>{"launchTimeUtc":"2026-12-01T23:30:00Z"}</c> (strict <see cref="LaunchClock.TryParseUtc"/>). 409 while a
    /// simulation of the mission is in Countdown or Running (paused included); otherwise the repository sets the planned
    /// T0 and resets the simulation anchor, actual launch time and simulated samples in one transaction.
    /// </summary>
    private static async Task<IResult> SetLaunchTimeAsync(
        string code,
        HttpContext context,
        IMissionRepository missions,
        ISimulationControl simulation,
        PlannedDocumentCache plannedCache,
        CancellationToken ct)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MissionEndpoints));
        if (!InputRules.IsValidMissionCode(code))
        {
            return ApiErrors.BadRequest(InputRules.MissionCodeRule);
        }

        var (body, error) = await JsonBody.ReadObjectAsync(context.Request, ct).ConfigureAwait(false);
        if (error is not null)
        {
            logger.LogWarning("T0 change of {MissionCode} rejected: invalid body", code);
            return error;
        }

        var (text, reason) = JsonBody.TryGetString(body, "launchTimeUtc", required: true);
        if (reason is null && !LaunchClock.TryParseUtc(text, out _))
        {
            reason = "'launchTimeUtc' must be an ISO 8601 UTC time ending with Z, e.g. 2026-12-01T23:30:00Z.";
        }

        if (reason is not null)
        {
            logger.LogWarning("T0 change of {MissionCode} rejected: {Reason}", code, reason);
            return ApiErrors.BadRequest(reason);
        }

        var launchTime = LaunchClock.ParseUtc(text!);
        var before = await missions.GetInfoAsync(code, ct).ConfigureAwait(false);
        if (before is null)
        {
            return UnknownMission(code);
        }

        // The state check and the write run under the simulation's operation lock, so a concurrent Start cannot slip in
        // between them (it waits, then starts with the new T0).
        bool found;
        try
        {
            found = await simulation
                .RunWhileInactiveAsync(before.MissionCode, token => missions.SetPlannedLaunchTimeAsync(before.MissionCode, launchTime, token), ct)
                .ConfigureAwait(false);
        }
        catch (SimulationStateException ex)
        {
            logger.LogWarning("T0 change of {MissionCode} rejected: {Reason}", code, ex.Message);
            return ApiErrors.Conflict(ex.Message);
        }

        if (!found)
        {
            return UnknownMission(code);
        }

        plannedCache.Invalidate(before.MissionCode);
        logger.LogInformation(
            "T0 of {MissionCode} changed: planned {OldPlanned:O} -> {NewPlanned:O} (effective before {OldEffective:O}); simulation anchor and simulated samples reset",
            before.MissionCode, before.PlannedLaunchTimeUtc, launchTime, before.LaunchTimeUtc);

        var after = await missions.GetInfoAsync(before.MissionCode, ct).ConfigureAwait(false);
        return after is null ? UnknownMission(code) : ApiErrors.Ok(MissionDto.From(after));
    }

    private static async Task<IResult> WithMissionAsync(string code, IMissionRepository missions, CancellationToken ct, Func<Mission, IResult> map)
    {
        if (!InputRules.IsValidMissionCode(code))
        {
            return ApiErrors.BadRequest(InputRules.MissionCodeRule);
        }

        var mission = await missions.GetPlannedMissionAsync(code, ct).ConfigureAwait(false);
        return mission is null ? UnknownMission(code) : map(mission);
    }

    /// <summary>Exact enum names only (case-insensitive); numbers are rejected.</summary>
    private static bool TryParseVehicle(string? text, out VehicleType vehicle)
    {
        vehicle = default;
        foreach (var value in Enum.GetValues<VehicleType>())
        {
            if (string.Equals(value.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                vehicle = value;
                return true;
            }
        }

        return false;
    }

    private static IResult UnknownMission(string code) => ApiErrors.NotFound($"Unknown mission '{code}'.");
}
