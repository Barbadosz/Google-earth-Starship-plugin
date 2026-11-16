using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

public class TrajectoryPointEntity
{
    public long Id { get; set; }
    public Guid TrajectoryId { get; set; }
    public double OffsetSeconds { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeMeters { get; set; }
    public double? VelocityMps { get; set; }
    public double? HeadingDegrees { get; set; }
    public double? PitchDegrees { get; set; }
    public double? RollDegrees { get; set; }
    public DataQuality Quality { get; set; }
    public string SourceName { get; set; } = "";

    public TrajectoryEntity Trajectory { get; set; } = null!;
}
