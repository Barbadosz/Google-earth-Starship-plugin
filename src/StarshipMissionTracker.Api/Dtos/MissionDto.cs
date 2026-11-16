using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Dtos;

/// <summary>
/// Mission header. <c>launchTimeUtc</c> is the effective T0 every document uses (simulation anchor, else planned);
/// <c>plannedLaunchTimeUtc</c> is the real plan (what <c>PUT launch-time</c> changes).
/// For a simulated mission <c>launchTimeUtc</c> may be the simulation anchor (<c>simulationLaunchTimeUtc</c>), also
/// after the run was stopped: a client must not read it as a real launch time. Only <c>actualLaunchTimeUtc</c> of a
/// mission with <c>isSimulated = false</c> is a real liftoff.
/// </summary>
public sealed record MissionDto(
    string MissionCode,
    string Name,
    string? Description,
    string Status,
    string LaunchTimeUtc,
    string PlannedLaunchTimeUtc,
    string? SimulationLaunchTimeUtc,
    string? ActualLaunchTimeUtc,
    bool IsSimulated,
    string UpdatedAtUtc,
    LaunchSiteDto? LaunchSite)
{
    public static MissionDto From(MissionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new MissionDto(
            info.MissionCode,
            info.Name,
            info.Description,
            info.Status.ToString(),
            ApiTime.ToText(info.LaunchTimeUtc),
            ApiTime.ToText(info.PlannedLaunchTimeUtc),
            ApiTime.ToText(info.SimulationLaunchTimeUtc),
            ApiTime.ToText(info.ActualLaunchTimeUtc),
            info.IsSimulated,
            ApiTime.ToText(info.UpdatedAtUtc),
            info.LaunchSite is { } site ? new LaunchSiteDto(site.Latitude, site.Longitude) : null);
    }
}

public sealed record LaunchSiteDto(double Latitude, double Longitude);
