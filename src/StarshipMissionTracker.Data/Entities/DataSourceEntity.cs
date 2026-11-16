using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Entities;

/// <summary>Where data comes from. <see cref="Reliability"/> is the quality the source itself can claim (Unknown when it varies per row).</summary>
public class DataSourceEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DataSourceType Type { get; set; }
    public string? Url { get; set; }
    public string? License { get; set; }
    public DataQuality Reliability { get; set; }
}
