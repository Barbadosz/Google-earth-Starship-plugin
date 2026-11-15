namespace StarshipMissionTracker.Core.Models;

/// <summary>Absolute URLs of the three child KML endpoints the master document links to.</summary>
public sealed record KmlEndpointUrls
{
    public KmlEndpointUrls(string plannedUrl, string historyUrl, string liveUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plannedUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(liveUrl);
        PlannedUrl = plannedUrl;
        HistoryUrl = historyUrl;
        LiveUrl = liveUrl;
    }

    public string PlannedUrl { get; }
    public string HistoryUrl { get; }
    public string LiveUrl { get; }
}
