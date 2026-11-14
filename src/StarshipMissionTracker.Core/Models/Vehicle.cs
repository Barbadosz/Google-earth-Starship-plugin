using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Core.Models;

/// <summary>A vehicle with at least one flight path (planned, actual, ...).</summary>
public sealed record Vehicle
{
    public Vehicle(string name, VehicleType type, string? modelPath, IReadOnlyList<FlightPath> flightPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var copy = Guard.CopyNoNulls(flightPaths, nameof(flightPaths));
        if (copy.Length == 0)
        {
            throw new ArgumentException("A vehicle needs at least one flight path.", nameof(flightPaths));
        }

        if (modelPath is not null)
        {
            Guard.RelativePath(modelPath, nameof(modelPath));
        }

        Name = name;
        Type = type;
        ModelPath = modelPath;
        FlightPaths = copy;
    }

    public string Name { get; }
    public VehicleType Type { get; }

    /// <summary>Relative path inside the KMZ (forward slashes, no '..', e.g. files/starship.dae), or null.</summary>
    public string? ModelPath { get; }

    public IReadOnlyList<FlightPath> FlightPaths { get; }
}
