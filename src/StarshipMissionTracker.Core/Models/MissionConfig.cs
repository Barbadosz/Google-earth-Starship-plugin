namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// Mission configuration file content (data/missions/&lt;code&gt;.json). LaunchTimeUtc is optional here
/// because the CLI may supply it; there is no default value.
/// </summary>
public sealed record MissionConfig
{
    public MissionConfig(
        string missionCode,
        string name,
        DateTimeOffset? launchTimeUtc,
        string? description,
        IReadOnlyList<MissionEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var eventCopy = Guard.CopyNoNulls(events, nameof(events));

        if (launchTimeUtc is { } t && t.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"Launch time must be UTC (offset 0), got offset {t.Offset}.", nameof(launchTimeUtc));
        }

        MissionCode = missionCode;
        Name = name;
        LaunchTimeUtc = launchTimeUtc;
        Description = description;
        Events = eventCopy;
    }

    public string MissionCode { get; }
    public string Name { get; }
    public DateTimeOffset? LaunchTimeUtc { get; }
    public string? Description { get; }
    public IReadOnlyList<MissionEvent> Events { get; }

    /// <summary>Returns a copy with T0 replaced (CLI --launch-time override).</summary>
    public MissionConfig WithLaunchTime(DateTimeOffset launchTimeUtc) =>
        new(MissionCode, Name, launchTimeUtc, Description, Events);
}
