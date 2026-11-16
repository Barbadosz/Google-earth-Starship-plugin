using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

public class MissionEventEntity
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public Guid? VehicleId { get; set; }
    public EventType Type { get; set; }
    public double OffsetSeconds { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AltitudeMeters { get; set; }
    public DataQuality Quality { get; set; }
    public string SourceName { get; set; } = "";

    public MissionEntity Mission { get; set; } = null!;
}
