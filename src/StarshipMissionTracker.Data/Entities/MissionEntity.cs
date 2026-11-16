using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

/// <summary>Persistence row of a mission. <see cref="PlannedLaunchTimeUtc"/> is the planned T0, <see cref="SimulationLaunchTimeUtc"/> the simulation anchor (UTC).</summary>
public class MissionEntity
{
    public Guid Id { get; set; }
    public string MissionCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public MissionStatus Status { get; set; }
    public DateTimeOffset PlannedLaunchTimeUtc { get; set; }
    public DateTimeOffset? ActualLaunchTimeUtc { get; set; }

    /// <summary>Wall launch time of the latest simulated run: the time axis of its stored samples (see MissionInfo.LaunchTimeUtc).</summary>
    public DateTimeOffset? SimulationLaunchTimeUtc { get; set; }

    public bool IsSimulated { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<VehicleEntity> Vehicles { get; set; } = [];
    public List<MissionEventEntity> Events { get; set; } = [];
}
