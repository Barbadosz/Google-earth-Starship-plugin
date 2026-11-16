using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

public class TelemetrySampleEntity
{
    public long Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AltitudeMeters { get; set; }
    public double? VelocityMps { get; set; }
    public double? HeadingDegrees { get; set; }
    public double? PitchDegrees { get; set; }
    public double? RollDegrees { get; set; }
    public DataQuality Quality { get; set; }
    public bool IsEstimated { get; set; }
    public Guid SourceId { get; set; }

    public VehicleEntity Vehicle { get; set; } = null!;
    public DataSourceEntity Source { get; set; } = null!;
}
