namespace StarshipMissionTracker.Core.Models;

/// <summary>NetworkLink refresh intervals; each must be at least <see cref="MinRefreshPeriod"/>, which must be positive.</summary>
public sealed record NetworkLinkSettings
{
    public NetworkLinkSettings(TimeSpan plannedRefresh, TimeSpan historyRefresh, TimeSpan liveRefresh, TimeSpan minRefreshPeriod)
    {
        if (minRefreshPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minRefreshPeriod), minRefreshPeriod, "Must be positive.");
        }

        Check(plannedRefresh, minRefreshPeriod, nameof(plannedRefresh));
        Check(historyRefresh, minRefreshPeriod, nameof(historyRefresh));
        Check(liveRefresh, minRefreshPeriod, nameof(liveRefresh));

        PlannedRefresh = plannedRefresh;
        HistoryRefresh = historyRefresh;
        LiveRefresh = liveRefresh;
        MinRefreshPeriod = minRefreshPeriod;
    }

    public TimeSpan PlannedRefresh { get; }
    public TimeSpan HistoryRefresh { get; }
    public TimeSpan LiveRefresh { get; }
    public TimeSpan MinRefreshPeriod { get; }

    private static void Check(TimeSpan value, TimeSpan min, string paramName)
    {
        if (value < min)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Refresh interval must be at least the minimum refresh period ({min}).");
        }
    }
}
