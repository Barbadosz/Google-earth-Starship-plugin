using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Services;

public enum KmlDocumentKind
{
    Master,
    Planned,
    History,
    Live
}

/// <summary>A built KML document; <see cref="Revision"/> is the NetworkLinkControl cookie value (null for the master).</summary>
public sealed record KmlDocument(string MissionCode, string Xml, long? Revision);

/// <summary>
/// Collects the inputs of the four server KML documents from the repositories and the simulation status and calls
/// <see cref="IDynamicKmlBuilder"/>. T0 is read only through <see cref="MissionInfo.LaunchTimeUtc"/> (the planned mission's
/// clock comes from the same value). A vehicle is STALE only while a run of this mission is Running and not paused, its
/// latest sample is older than <see cref="LiveOptions.StaleAfterSeconds"/> (by ReceivedAtUtc) and it has not reached the
/// end of its planned path yet.
/// </summary>
public sealed class KmlDocumentService(
    IMissionRepository missions,
    ITelemetryRepository telemetry,
    ISimulationControl simulation,
    IDynamicKmlBuilder builder,
    PlannedDocumentCache plannedCache,
    RevisionCounter revisions,
    StaleTelemetryTracker staleTracker,
    TimeProvider timeProvider,
    IOptions<NetworkLinkOptions> networkLinkOptions,
    IOptions<LiveOptions> liveOptions,
    IOptions<HistoryOptions> historyOptions,
    ILogger<KmlDocumentService> logger)
{
    /// <summary>Tolerance (mission seconds) for "the latest sample is the vehicle's final planned point".</summary>
    private const double EndToleranceSeconds = 1e-3;

    /// <summary>
    /// Builds the document for the mission (<paramref name="missionCode"/> null = current mission). Null when the
    /// mission is unknown or there is no mission at all.
    /// </summary>
    public async Task<KmlDocument?> BuildAsync(KmlDocumentKind kind, string? missionCode, ServerUrls urls, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);
        var info = await GetInfoAsync(missionCode, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        var settings = networkLinkOptions.Value.ToSettings();
        var assets = new KmlAssetUrls(urls.ModelBaseUrl, urls.IconBaseUrl);
        KmlDocument? document;
        if (kind == KmlDocumentKind.Master)
        {
            var endpoints = new KmlEndpointUrls(
                urls.Kml(ServerUrls.PlannedPath, info.MissionCode),
                urls.Kml(ServerUrls.HistoryPath, info.MissionCode),
                urls.Kml(ServerUrls.LivePath, info.MissionCode));
            document = new KmlDocument(info.MissionCode, builder.BuildMaster(info, endpoints, settings, assets), null);
        }
        else
        {
            document = await BuildChildAsync(kind, info, urls, assets, settings.MinRefreshPeriod, cancellationToken).ConfigureAwait(false);
        }

        if (document is not null)
        {
            logger.LogDebug(
                "KML {Kind} for {MissionCode}: {Length} chars, revision {Revision}", kind, document.MissionCode, document.Xml.Length, document.Revision);
        }

        return document;
    }

    private async Task<KmlDocument?> BuildChildAsync(
        KmlDocumentKind kind, MissionInfo info, ServerUrls urls, KmlAssetUrls assets, TimeSpan minRefresh, CancellationToken cancellationToken)
    {
        var prepared = await GetPreparedAsync(info, cancellationToken).ConfigureAwait(false);
        if (prepared is null)
        {
            return null;
        }

        (info, var mission) = prepared.Value;
        switch (kind)
        {
            case KmlDocumentKind.Planned:
            {
                var revision = revisions.Next();
                var controlUrl = info.IsSimulated ? urls.ControlUrl : null;
                var xml = builder.BuildPlanned(info, mission.Mission, mission.DensifiedPaths, assets, revision, minRefresh, controlUrl);
                return new KmlDocument(info.MissionCode, xml, revision);
            }

            case KmlDocumentKind.History:
            {
                var vehicles = await ActualVehiclesAsync(info, mission.Mission, cancellationToken).ConfigureAwait(false);
                var revision = revisions.Next();
                return new KmlDocument(info.MissionCode, builder.BuildHistory(info, vehicles, assets, revision, minRefresh), revision);
            }

            case KmlDocumentKind.Live:
            {
                var now = timeProvider.GetUtcNow().ToUniversalTime();
                var latest = await telemetry.GetLatestAsync(info.MissionCode, cancellationToken).ConfigureAwait(false);
                var states = LiveStates(info, mission.Mission, latest, simulation.GetStatus(), now);
                var revision = revisions.Next();
                return new KmlDocument(info.MissionCode, builder.BuildLive(info, states, now, assets, revision, minRefresh), revision);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a child document.");
        }
    }

    /// <summary>Live states per planned vehicle (ordered by type); the stale rule is described on the class.</summary>
    private IReadOnlyList<LiveVehicleState> LiveStates(
        MissionInfo info, Mission mission, IReadOnlyList<TelemetrySample> latest, SimulationStatus status, DateTimeOffset nowUtc)
    {
        var staleAfter = TimeSpan.FromSeconds(liveOptions.Value.StaleAfterSeconds);
        var runActive = status.State == SimulationState.Running
                        && !status.IsPaused
                        && string.Equals(status.MissionCode, info.MissionCode, StringComparison.OrdinalIgnoreCase);
        var result = new List<LiveVehicleState>();
        foreach (var vehicle in mission.Vehicles.OrderBy(v => v.Type))
        {
            var sample = latest.Where(s => s.Vehicle == vehicle.Type).MaxBy(s => s.TimestampUtc);
            if (sample is null)
            {
                result.Add(new LiveVehicleState(vehicle, null, null, false));
                continue;
            }

            var age = nowUtc - sample.ReceivedAtUtc;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            var stale = runActive && age > staleAfter && !ReachedEnd(vehicle, sample, info.LaunchTimeUtc);
            // Warning once per change (stale begins, or a new latest sample is stale again); every poll in between is Debug.
            if (stale && staleTracker.MarkStale(info.MissionCode, vehicle.Type, sample.TimestampUtc))
            {
                logger.LogWarning(
                    "Stale telemetry: {MissionCode} {Vehicle} latest sample {TimestampUtc:O} received {Age} ago (threshold {StaleAfter})",
                    info.MissionCode, vehicle.Type, sample.TimestampUtc, age, staleAfter);
            }
            else if (stale)
            {
                logger.LogDebug(
                    "Still stale: {MissionCode} {Vehicle} latest sample {TimestampUtc:O} received {Age} ago",
                    info.MissionCode, vehicle.Type, sample.TimestampUtc, age);
            }
            else if (staleTracker.Clear(info.MissionCode, vehicle.Type))
            {
                logger.LogInformation("Telemetry no longer stale: {MissionCode} {Vehicle}", info.MissionCode, vehicle.Type);
            }

            result.Add(new LiveVehicleState(vehicle, sample, age, stale));
        }

        return result;
    }

    /// <summary>
    /// True when the sample is at (or past) the last point of the vehicle's planned path: the simulator stops sampling a
    /// finished vehicle (e.g. Super Heavy after landing), so its old sample is expected, not stale.
    /// </summary>
    private static bool ReachedEnd(Vehicle vehicle, TelemetrySample sample, DateTimeOffset launchTimeUtc)
    {
        var lastPlanned = vehicle.FlightPaths
            .Where(p => p.Type == TrajectoryType.Planned && p.Points.Count > 0)
            .Select(p => (double?)p.Points[^1].OffsetSeconds)
            .Max();
        if (lastPlanned is not { } last)
        {
            return false;
        }

        var offset = (sample.TimestampUtc - launchTimeUtc).TotalSeconds;
        return offset >= last - EndToleranceSeconds;
    }

    private async Task<IReadOnlyList<Vehicle>> ActualVehiclesAsync(MissionInfo info, Mission mission, CancellationToken cancellationToken)
    {
        var maxPoints = historyOptions.Value.MaxPointsPerVehicle;
        var result = new List<Vehicle>();
        foreach (var vehicle in mission.Vehicles.OrderBy(v => v.Type))
        {
            var path = await telemetry.GetHistoryAsync(info.MissionCode, vehicle.Type, maxPoints, cancellationToken).ConfigureAwait(false);
            if (path is not null)
            {
                result.Add(new Vehicle(vehicle.Name, vehicle.Type, vehicle.ModelPath, [path]));
            }
        }

        return result;
    }

    /// <summary>
    /// The header and the prepared mission built from the same effective T0. One retry with a fresh header when the T0
    /// changed between the two reads.
    /// </summary>
    private async Task<(MissionInfo Info, PreparedMission Mission)?> GetPreparedAsync(MissionInfo info, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var code = info.MissionCode;
                var prepared = await plannedCache
                    .GetOrCreateAsync(info, ct => missions.GetPlannedMissionAsync(code, ct), cancellationToken)
                    .ConfigureAwait(false);
                return prepared is null ? null : (info, prepared);
            }
            catch (MissionChangedException ex) when (attempt == 1)
            {
                logger.LogInformation("Retrying KML build for {MissionCode}: {Reason}", info.MissionCode, ex.Message);
                var fresh = await missions.GetInfoAsync(info.MissionCode, cancellationToken).ConfigureAwait(false);
                if (fresh is null)
                {
                    return null;
                }

                info = fresh;
            }
        }
    }

    private Task<MissionInfo?> GetInfoAsync(string? missionCode, CancellationToken cancellationToken) =>
        missionCode is null
            ? missions.GetCurrentInfoAsync(cancellationToken)
            : missions.GetInfoAsync(missionCode, cancellationToken);
}
