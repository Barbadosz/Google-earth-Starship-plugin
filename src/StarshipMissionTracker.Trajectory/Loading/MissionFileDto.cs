using System.Text.Json.Serialization;

namespace StarshipMissionTracker.Trajectory.Loading;

/// <summary>JSON shape of <c>data/missions/&lt;code&gt;.json</c> (strings are parsed into enums by the loader).</summary>
internal sealed class MissionFileDto
{
    [JsonPropertyName("missionCode")] public string? MissionCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("launchTimeUtc")] public string? LaunchTimeUtc { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("vehicles")] public List<VehicleDto>? Vehicles { get; set; }
    [JsonPropertyName("events")] public List<EventDto>? Events { get; set; }

    internal sealed class VehicleDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("modelPath")] public string? ModelPath { get; set; }
    }

    internal sealed class EventDto
    {
        [JsonPropertyName("t")] public double? T { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("lat")] public double? Lat { get; set; }
        [JsonPropertyName("lon")] public double? Lon { get; set; }
        [JsonPropertyName("altM")] public double? AltM { get; set; }
        [JsonPropertyName("vehicle")] public string? Vehicle { get; set; }
        [JsonPropertyName("quality")] public string? Quality { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
    }
}
