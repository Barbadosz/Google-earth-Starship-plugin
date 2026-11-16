namespace StarshipMissionTracker.Api.Http;

/// <summary>
/// Absolute URLs derived from the incoming request (scheme, Host, PathBase), so a changed port or host name works
/// without configuration. Paths of the KML endpoints, assets and the control page are fixed here.
/// </summary>
public sealed record ServerUrls
{
    public const string MasterPath = "/kml/master";
    public const string PlannedPath = "/kml/planned";
    public const string HistoryPath = "/kml/history";
    public const string LivePath = "/kml/live";

    /// <summary>Request path of the static asset files (served from the repository's <c>assets/</c> folder).</summary>
    public const string AssetsRequestPath = "/assets";

    public const string ModelsPath = AssetsRequestPath + "/models";
    public const string IconsPath = AssetsRequestPath + "/icons";
    public const string ControlPath = "/control";
    public const string ControlPagePath = "/control/index.html";

    public ServerUrls(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        BaseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>Scheme + host + path base, without trailing slash (e.g. <c>http://127.0.0.1:8080</c>).</summary>
    public string BaseUrl { get; }

    public string ModelBaseUrl => BaseUrl + ModelsPath;
    public string IconBaseUrl => BaseUrl + IconsPath;
    public string ControlUrl => BaseUrl + ControlPath;

    /// <summary>Absolute KML endpoint URL with the mission code as query parameter.</summary>
    public string Kml(string path, string missionCode) => $"{BaseUrl}{path}?mission={Uri.EscapeDataString(missionCode)}";

    /// <summary>
    /// The base URL of the request. A Host that <see cref="LocalHostRule"/> does not accept (missing, or a foreign name
    /// as in DNS rebinding) is never copied into URLs: the fallback is <c>127.0.0.1</c> with the port the connection
    /// arrived on (no port when unknown). The request is still answered (read-only KML, no secrets), so a plain Google
    /// Earth link keeps working.
    /// </summary>
    public static ServerUrls FromRequest(HttpRequest request, IReadOnlyCollection<string> allowedHostNames) =>
        FromRequest(request, allowedHostNames, out _);

    /// <param name="usedFallback">True when the request's Host was not accepted and the loopback fallback was used.</param>
    public static ServerUrls FromRequest(HttpRequest request, IReadOnlyCollection<string> allowedHostNames, out bool usedFallback)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(allowedHostNames);
        usedFallback = !LocalHostRule.IsAllowed(request.Host, allowedHostNames);
        string host;
        if (!usedFallback)
        {
            host = request.Host.Value!;
        }
        else
        {
            var port = request.HttpContext.Connection.LocalPort;
            host = port > 0 ? FormattableString.Invariant($"127.0.0.1:{port}") : "127.0.0.1";
        }

        return new ServerUrls($"{request.Scheme}://{host}{request.PathBase}");
    }
}
