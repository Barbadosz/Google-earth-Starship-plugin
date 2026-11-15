using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

/// <summary>Builds the KML documents of the server mode (plain KML strings; no network, no database, no clock).</summary>
public interface IDynamicKmlBuilder
{
    /// <summary>Master document with three NetworkLinks.</summary>
    string BuildMaster(MissionInfo info, KmlEndpointUrls urls, NetworkLinkSettings settings, KmlAssetUrls? assets = null);

    /// <summary>
    /// Mission information, planned trajectory and events. <paramref name="controlUrl"/> (optional) adds a plain-text
    /// "Simulation control" line to the Mission Information balloon.
    /// </summary>
    string BuildPlanned(
        MissionInfo info,
        Mission mission,
        IReadOnlySet<FlightPath> densified,
        KmlAssetUrls assets,
        long revision,
        TimeSpan minRefresh,
        string? controlUrl = null);

    /// <summary>Actual trajectory per vehicle; the vehicles carry their Actual flight path.</summary>
    string BuildHistory(
        MissionInfo info,
        IReadOnlyList<Vehicle> vehiclesWithActualPath,
        KmlAssetUrls assets,
        long revision,
        TimeSpan minRefresh);

    /// <summary>Vehicles at their latest sample. <paramref name="nowUtc"/> is a parameter (no clock inside).</summary>
    string BuildLive(
        MissionInfo info,
        IReadOnlyList<LiveVehicleState> vehicles,
        DateTimeOffset nowUtc,
        KmlAssetUrls assets,
        long revision,
        TimeSpan minRefresh);
}
