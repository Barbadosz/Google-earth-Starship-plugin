using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Options;

/// <summary>
/// NetworkLink refresh intervals of the master document (section "NetworkLinks"). Google Earth Pro reads them only when
/// the link is loaded. <see cref="MinRefreshPeriodSeconds"/> goes into every child document's NetworkLinkControl.
/// </summary>
public sealed class NetworkLinkOptions
{
    public const string SectionName = "NetworkLinks";

    public double PlannedRefreshSeconds { get; set; } = 60;

    public double HistoryRefreshSeconds { get; set; } = 15;

    public double LiveRefreshSeconds { get; set; } = 5;

    public double MinRefreshPeriodSeconds { get; set; } = 2;

    /// <summary>Null when valid, else the reason (for <c>Validate(...)</c> + <c>ValidateOnStart</c>).</summary>
    public string? Validate()
    {
        foreach (var (name, value) in new[]
                 {
                     (nameof(PlannedRefreshSeconds), PlannedRefreshSeconds),
                     (nameof(HistoryRefreshSeconds), HistoryRefreshSeconds),
                     (nameof(LiveRefreshSeconds), LiveRefreshSeconds),
                     (nameof(MinRefreshPeriodSeconds), MinRefreshPeriodSeconds),
                 })
        {
            if (!double.IsFinite(value) || value <= 0 || value > 86_400)
            {
                return $"{SectionName}:{name} must be a finite number of seconds in (0, 86400], got {value}.";
            }
        }

        if (PlannedRefreshSeconds < MinRefreshPeriodSeconds
            || HistoryRefreshSeconds < MinRefreshPeriodSeconds
            || LiveRefreshSeconds < MinRefreshPeriodSeconds)
        {
            return $"{SectionName}: every refresh interval must be at least MinRefreshPeriodSeconds ({MinRefreshPeriodSeconds}).";
        }

        return null;
    }

    /// <exception cref="ArgumentOutOfRangeException">The values are invalid (see <see cref="Validate"/>).</exception>
    public NetworkLinkSettings ToSettings() => new(
        TimeSpan.FromSeconds(PlannedRefreshSeconds),
        TimeSpan.FromSeconds(HistoryRefreshSeconds),
        TimeSpan.FromSeconds(LiveRefreshSeconds),
        TimeSpan.FromSeconds(MinRefreshPeriodSeconds));
}
