using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Core.Models;

/// <summary>A flight event at a T+ offset. Position is optional but latitude and longitude come together.</summary>
public sealed record MissionEvent
{
    public MissionEvent(
        double offsetSeconds,
        EventType type,
        string name,
        string? description,
        double? latitude,
        double? longitude,
        double? altitudeMeters,
        VehicleType? vehicle,
        DataQuality quality,
        string sourceName)
    {
        Guard.Finite(offsetSeconds, nameof(offsetSeconds));

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ArgumentException("Latitude and longitude must both be set or both be null.");
        }

        if (latitude is { } lat)
        {
            Guard.Latitude(lat, nameof(latitude));
        }

        if (longitude is { } lon)
        {
            longitude = Guard.Longitude(lon, nameof(longitude));
        }

        if (altitudeMeters is { } alt)
        {
            Guard.Finite(alt, nameof(altitudeMeters));
        }

        OffsetSeconds = offsetSeconds;
        Type = type;
        Name = name;
        Description = description;
        Latitude = latitude;
        Longitude = longitude;
        AltitudeMeters = altitudeMeters;
        Vehicle = vehicle;
        Quality = quality;
        SourceName = sourceName;
    }

    public double OffsetSeconds { get; }
    public EventType Type { get; }
    public string Name { get; }
    public string? Description { get; }
    public double? Latitude { get; }

    /// <summary>Degrees in [-180, 180); +180 is normalised to -180 (antimeridian rule).</summary>
    public double? Longitude { get; }

    /// <summary>Metres above mean sea level (EGM96 geoid), matching KML altitudeMode "absolute".</summary>
    public double? AltitudeMeters { get; }

    /// <summary>The vehicle the event belongs to; null = whole stack / mission.</summary>
    public VehicleType? Vehicle { get; }

    public DataQuality Quality { get; }
    public string SourceName { get; }
    public bool HasPosition => Latitude.HasValue;
}
