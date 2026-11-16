namespace StarshipMissionTracker.Api.Options;

/// <summary>
/// Host names accepted in the request <c>Host</c> header (section "HostFilter"). Mutating endpoints reject any other
/// Host with 403 and the KML URLs never carry it (DNS rebinding protection, see <see cref="Http.LocalHostRule"/>).
/// A loopback IP literal (127.x.x.x, [::1]) is always accepted. The configuration binder appends configured entries to
/// the defaults, so configuration can only add names (e.g. the computer's own name), never remove localhost.
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
        }

        return null;
    }
}
