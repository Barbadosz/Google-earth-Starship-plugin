using System.Net;

namespace StarshipMissionTracker.Api.Http;

/// <summary>
/// The request <c>Host</c> check against DNS rebinding: a hostile page whose name re-resolves to 127.0.0.1 is
/// same-origin for the browser (no CORS preflight), and its connection comes from loopback, but its <c>Host</c> header
/// still carries the hostile name. Accepted: a loopback IP literal, or a name in the allow-list
/// (<see cref="Options.HostFilterOptions"/>, case-insensitive, port ignored). A missing Host is rejected (fails closed).
/// </summary>
public static class LocalHostRule
{
    /// <summary>Longest Host value written to the log (the header is caller-controlled).</summary>
    private const int MaxLoggedLength = 100;

    public static bool IsAllowed(HostString host, IReadOnlyCollection<string> allowedHostNames)
    {
        ArgumentNullException.ThrowIfNull(allowedHostNames);
        if (!host.HasValue)
        {
            return false;
        }

        var name = host.Host;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var literal = name.Length > 2 && name[0] == '[' && name[^1] == ']' ? name[1..^1] : name;
        if (IPAddress.TryParse(literal, out var address) && LoopbackOnlyFilter.IsLoopback(address))
        {
            return true;
        }

        return allowedHostNames.Any(allowed => string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The Host value shortened for a log line.</summary>
    public static string ForLog(HostString host)
    {
        if (!host.HasValue)
        {
            return "(none)";
        }

        var value = host.Value;
        return value.Length <= MaxLoggedLength ? value : value[..MaxLoggedLength] + "...";
    }
}
