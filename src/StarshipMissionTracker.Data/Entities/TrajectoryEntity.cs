using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

/// <summary>A stored flight path (Planned or Reconstructed) of one vehicle. Actual paths live in telemetry samples.</summary>
public class TrajectoryEntity
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public Guid VehicleId { get; set; }
    public TrajectoryType Type { get; set; }
    public DataQuality Quality { get; set; }
    public Guid SourceId { get; set; }
    public string SourceName { get; set; } = "";
    public string? License { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public MissionEntity Mission { get; set; } = null!;
    public VehicleEntity Vehicle { get; set; } = null!;
    public DataSourceEntity Source { get; set; } = null!;
    public List<TrajectoryPointEntity> Points { get; set; } = [];
}
