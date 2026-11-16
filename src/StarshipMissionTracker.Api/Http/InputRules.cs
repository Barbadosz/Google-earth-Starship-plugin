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

    /// <summary>
    /// Accepted planned T0 range (UTC, inclusive start, exclusive end): years 2000..2199. Far inside DateTime's
    /// 0001..9999, so T0 + any event/trajectory offset (hours..days) never overflows.
    /// </summary>
    public static readonly DateTimeOffset MinLaunchTimeUtc = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc cref="MinLaunchTimeUtc"/>
    public static readonly DateTimeOffset MaxLaunchTimeUtcExclusive = new(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsLaunchTimeInRange(DateTimeOffset t) => t >= MinLaunchTimeUtc && t < MaxLaunchTimeUtcExclusive;

    public static string LaunchTimeRangeRule => "'launchTimeUtc' must be between 2000-01-01T00:00:00Z (inclusive) and 2200-01-01T00:00:00Z (exclusive).";

    /// <summary>True for <c>[A-Za-z0-9._-]{1,64}</c>.</summary>
    public static bool IsValidMissionCode(string? code) => code is not null && MissionCodeRegex().IsMatch(code);

    public static string MissionCodeRule => "Mission code must match [A-Za-z0-9._-]{1,64}.";

    // \z, not $: '$' also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex MissionCodeRegex();
}
