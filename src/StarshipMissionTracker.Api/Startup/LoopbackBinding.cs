using System.Net;

namespace StarshipMissionTracker.Api.Startup;

/// <summary>
/// The server must listen on loopback only (plan D3, review N-29): the mutating endpoints have no authentication. Checks
/// every configured listen address (<c>Urls</c> / <c>--urls</c> / <c>ASPNETCORE_URLS</c>, <c>Kestrel:Endpoints:*:Url</c>,
/// and the port-only settings, which bind every interface) before the host is built.
/// </summary>
public static class LoopbackBinding
{
    /// <summary>Null when every configured address is loopback (or nothing is configured), else the reason.</summary>
    public static string? Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var urls = Split(configuration["urls"]);
        if (urls.Count == 0)
        {
            foreach (var key in new[] { "http_ports", "https_ports" })
            {
                if (!string.IsNullOrWhiteSpace(configuration[key]))
                {
                    return $"'{key}' binds every network interface; configure a loopback URL instead (Urls, e.g. http://127.0.0.1:8080).";
                }
            }
        }

        foreach (var endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            if (endpoint["Url"] is { } url)
            {
                urls.Add(url);
            }
        }

        foreach (var url in urls)
        {
            if (!IsLoopback(url))
            {
                return $"Listen address '{url}' is not loopback; the server may only bind to localhost, 127.0.0.1 or [::1].";
            }
        }

        return null;
    }

    /// <summary>True for <c>localhost</c> and loopback IP literals; false for wildcards (<c>*</c>, <c>+</c>, 0.0.0.0, [::]) and other hosts.</summary>
    public static bool IsLoopback(string url)
    {
        BindingAddress address;
        try
        {
            address = BindingAddress.Parse(url);
        }
        catch (FormatException)
        {
            return false;
        }

        if (address.IsUnixPipe || address.IsNamedPipe)
        {
            return true; // Local by nature.
        }

        var host = address.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var literal = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        return IPAddress.TryParse(literal, out var ip) && IPAddress.IsLoopback(ip);
    }

    private static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
