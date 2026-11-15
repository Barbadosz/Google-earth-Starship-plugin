namespace StarshipMissionTracker.Core.Models;

/// <summary>Absolute base URLs (trailing slash optional) of the served 3D models and icons.</summary>
public sealed record KmlAssetUrls
{
    public KmlAssetUrls(string modelBaseUrl, string iconBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconBaseUrl);
        ModelBaseUrl = modelBaseUrl;
        IconBaseUrl = iconBaseUrl;
    }

    public string ModelBaseUrl { get; }
    public string IconBaseUrl { get; }
}
