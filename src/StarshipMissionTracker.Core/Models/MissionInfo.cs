using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// Lightweight mission header as stored in the database (no paths). <see cref="PlannedLaunchTimeUtc"/> is the real plan,
/// <see cref="SimulationLaunchTimeUtc"/> the wall launch time of the latest simulated run; readers take T0 from
/// <see cref="LaunchTimeUtc"/>. Like every instant here, all must be UTC (offset zero).
/// <see cref="LaunchSite"/> is an optional (latitude, longitude) pair.
/// </summary>
public sealed record MissionInfo
{
    public MissionInfo(
        Guid id,
        string missionCode,
        string name,
        string? description,
        MissionStatus status,
        DateTimeOffset plannedLaunchTimeUtc,
        DateTimeOffset? actualLaunchTimeUtc,
        bool isSimulated,
        DateTimeOffset updatedAtUtc,
        (double Latitude, double Longitude)? launchSite = null,
        DateTimeOffset? simulationLaunchTimeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UtcCheck.Require(plannedLaunchTimeUtc, nameof(plannedLaunchTimeUtc));
        if (actualLaunchTimeUtc is { } actual)
        {
            UtcCheck.Require(actual, nameof(actualLaunchTimeUtc));
        }

        if (simulationLaunchTimeUtc is { } anchor)
        {
            UtcCheck.Require(anchor, nameof(simulationLaunchTimeUtc));
        }

        UtcCheck.Require(updatedAtUtc, nameof(updatedAtUtc));

        if (launchSite is { } site)
        {
            Guard.Latitude(site.Latitude, nameof(launchSite));
            launchSite = (site.Latitude, Guard.Longitude(site.Longitude, nameof(launchSite)));
        }

        Id = id;
        MissionCode = missionCode;
        Name = name;
        Description = description;
        Status = status;
        PlannedLaunchTimeUtc = plannedLaunchTimeUtc;
        ActualLaunchTimeUtc = actualLaunchTimeUtc;
        IsSimulated = isSimulated;
        UpdatedAtUtc = updatedAtUtc;
        LaunchSite = launchSite;
        SimulationLaunchTimeUtc = simulationLaunchTimeUtc;
    }

    public Guid Id { get; }
    public string MissionCode { get; }
    public string Name { get; }
    public string? Description { get; }
    public MissionStatus Status { get; }

    /// <summary>
    /// The planned T0 (UTC), written only by import and the T0 edit. Not necessarily the time axis of stored samples:
    /// readers use <see cref="LaunchTimeUtc"/>.
    /// </summary>
    public DateTimeOffset PlannedLaunchTimeUtc { get; }

    public DateTimeOffset? ActualLaunchTimeUtc { get; }
    public bool IsSimulated { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public (double Latitude, double Longitude)? LaunchSite { get; }

    /// <summary>
    /// Wall launch time (UTC) of the latest simulated run, the anchor of its stored samples; null when the mission was
    /// never simulated or its T0 was edited since.
    /// </summary>
    public DateTimeOffset? SimulationLaunchTimeUtc { get; }

    /// <summary>Effective T0 that every reader uses as the time axis: the simulation anchor if set, else the planned T0.</summary>
    public DateTimeOffset LaunchTimeUtc => SimulationLaunchTimeUtc ?? PlannedLaunchTimeUtc;
}

/// <summary>UTC-instant check shared by the M2 models (Guard.cs is a hot spot, so this lives next to its users).</summary>
internal static class UtcCheck
{
    public static void Require(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"'{paramName}' must be a UTC instant (offset zero), got offset {value.Offset}.", paramName);
        }
    }
}
