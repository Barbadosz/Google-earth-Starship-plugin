using System.Text.Json.Serialization;

namespace StarshipMissionTracker.Trajectory.Loading;

/// <summary>JSON shape of <c>data/trajectories/&lt;code&gt;-&lt;vehicle&gt;-planned.json</c>.</summary>
internal sealed class TrajectoryFileDto
{
    [JsonPropertyName("vehicle")] public string? Vehicle { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("license")] public string? License { get; set; }
    [JsonPropertyName("points")] public List<PointDto>? Points { get; set; }

    internal sealed class PointDto
    {
        [JsonPropertyName("t")] public double? T { get; set; }
        [JsonPropertyName("lat")] public double? Lat { get; set; }
        [JsonPropertyName("lon")] public double? Lon { get; set; }
        [JsonPropertyName("altM")] public double? AltM { get; set; }
        [JsonPropertyName("velocityMps")] public double? VelocityMps { get; set; }
        [JsonPropertyName("headingDeg")] public double? HeadingDeg { get; set; }
        [JsonPropertyName("pitchDeg")] public double? PitchDeg { get; set; }
        [JsonPropertyName("rollDeg")] public double? RollDeg { get; set; }
        [JsonPropertyName("quality")] public string? Quality { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
    }
}
