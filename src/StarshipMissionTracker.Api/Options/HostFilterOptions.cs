namespace StarshipMissionTracker.Api.Options;

/// <summary>
/// Host names accepted in the request <c>Host</c> header (section "HostFilter"). Mutating endpoints reject any other
/// Host with 403 and the KML URLs never carry it (DNS rebinding protection, see <see cref="Http.LocalHostRule"/>).
/// A loopback IP literal (127.x.x.x, [::1]) is always accepted. The configuration binder appends configured entries to
/// the defaults, so configuration can only add names (e.g. the computer's own name), never remove localhost.
/// Note: the framework's <c>AllowedHosts</c> (appsettings, HostFiltering middleware, answers 400 first) is a separate
/// list. When adding a host name, add it to BOTH; the framework list is the stricter outer gate, this one is defence in depth.
/// </summary>
public sealed class HostFilterOptions
{
    public const string SectionName = "HostFilter";

    public string[] AllowedHostNames { get; set; } = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>Null when valid, else the reason (for <c>Validate(...)</c> + <c>ValidateOnStart</c>).</summary>
    public string? Validate()
    {
        if (AllowedHostNames is null || AllowedHostNames.Length == 0)
        {
            return $"{SectionName}:{nameof(AllowedHostNames)} must not be empty.";
        }

        foreach (var name in AllowedHostNames)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('*', StringComparison.Ordinal) || name.Any(char.IsWhiteSpace))
            {
                return $"{SectionName}:{nameof(AllowedHostNames)} entries must be plain host names without wildcards or spaces, got '{name}'.";
            }

            // HostString.Host never carries the port and keeps IPv6 literals in brackets, so "localhost:8080" or "::1"
            // could never match: reject them at startup instead of answering every request with a silent 403.
            var bracketed = name.StartsWith('[') && name.EndsWith(']')
                && System.Net.IPAddress.TryParse(name.AsSpan(1, name.Length - 2), out var literal)
                && literal.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            if (name.Contains(':', StringComparison.Ordinal) && !bracketed)
            {
                return $"{SectionName}:{nameof(AllowedHostNames)} entries must not contain a port, and IPv6 literals need brackets (e.g. '[::1]'), got '{name}'.";
            }

            if (!bracketed && (name.Contains('[', StringComparison.Ordinal) || name.Contains(']', StringComparison.Ordinal)))
            {
                return $"{SectionName}:{nameof(AllowedHostNames)} entries have unbalanced brackets, got '{name}'.";
            }
        }

        return null;
    }
}
