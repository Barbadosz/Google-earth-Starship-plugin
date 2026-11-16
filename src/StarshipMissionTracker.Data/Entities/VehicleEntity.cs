using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

public class VehicleEntity
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public string Name { get; set; } = "";
    public VehicleType Type { get; set; }
    public string? ModelPath { get; set; }

    public MissionEntity Mission { get; set; } = null!;
    public List<TrajectoryEntity> Trajectories { get; set; } = [];
}
