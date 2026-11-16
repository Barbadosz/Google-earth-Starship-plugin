using System.Text.RegularExpressions;

namespace StarshipMissionTracker.Api.Http;

/// <summary>Strict input rules at the API boundary (plan section 9).</summary>
public static partial class InputRules
{
    public const int MaxSampleLimit = 10_000;
    public const int DefaultSampleLimit = 1_000;

    /// <summary>Longest countdown accepted by start (one day of wall time).</summary>
    public const double MaxCountdownSeconds = 86_400;

    public const double DefaultCountdownSeconds = 10;

    /// <summary>True for <c>[A-Za-z0-9._-]{1,64}</c>.</summary>
    public static bool IsValidMissionCode(string? code) => code is not null && MissionCodeRegex().IsMatch(code);

    public static string MissionCodeRule => "Mission code must match [A-Za-z0-9._-]{1,64}.";

    // \z, not $: '$' also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex MissionCodeRegex();
}
