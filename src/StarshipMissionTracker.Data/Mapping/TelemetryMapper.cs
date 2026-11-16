using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Mapping;

internal static class TelemetryMapper
{
    public static TelemetrySampleEntity ToEntity(TelemetrySample s, Guid vehicleId, Guid sourceId) =>
        new()
        {
            VehicleId = vehicleId,
            TimestampUtc = s.TimestampUtc,
            ReceivedAtUtc = s.ReceivedAtUtc,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            AltitudeMeters = s.AltitudeMeters,
            VelocityMps = s.VelocityMetersPerSecond,
            HeadingDegrees = s.HeadingDegrees,
            PitchDegrees = s.PitchDegrees,
            RollDegrees = s.RollDegrees,
            Quality = s.Quality,
            IsEstimated = s.IsEstimated,
            SourceId = sourceId,
        };

    /// <summary>Needs Vehicle and Source loaded.</summary>
    public static TelemetrySample ToModel(TelemetrySampleEntity e) =>
        new(
            e.Vehicle.Type,
            e.TimestampUtc,
            e.ReceivedAtUtc,
            e.Latitude,
            e.Longitude,
            e.AltitudeMeters,
            e.VelocityMps,
            e.HeadingDegrees,
            e.PitchDegrees,
            e.RollDegrees,
            e.Quality,
            e.Source.Name);
}
