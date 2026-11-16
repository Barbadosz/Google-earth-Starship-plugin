using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

/// <summary>Persistence row of a mission. <see cref="PlannedLaunchTimeUtc"/> is T0 (UTC).</summary>
public class MissionEntity
{
    public Guid Id { get; set; }
    public string MissionCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public MissionStatus Status { get; set; }
    public DateTimeOffset PlannedLaunchTimeUtc { get; set; }
    public DateTimeOffset? ActualLaunchTimeUtc { get; set; }
    public bool IsSimulated { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<VehicleEntity> Vehicles { get; set; } = [];
    public List<MissionEventEntity> Events { get; set; } = [];
}
