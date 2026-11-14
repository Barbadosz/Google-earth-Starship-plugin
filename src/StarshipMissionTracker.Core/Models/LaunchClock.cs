using System.Globalization;

namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// Converts T+ offsets (seconds from launch) to absolute UTC times: T0 + offset.
/// T0 must be UTC. There is no default T0 and the clock never reads the system time.
/// </summary>
public sealed class LaunchClock
{
    public const string SecondsFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    public const string MillisecondsFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public LaunchClock(DateTimeOffset launchTimeUtc)
    {
        if (launchTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"Launch time must be UTC (offset 0), got offset {launchTimeUtc.Offset}.", nameof(launchTimeUtc));
        }

        LaunchTimeUtc = launchTimeUtc;
    }

    public DateTimeOffset LaunchTimeUtc { get; }

    /// <summary>
    /// Parses strictly yyyy-MM-ddTHH:mm:ssZ or yyyy-MM-ddTHH:mm:ss.f..fZ (up to 7 fraction digits), invariant culture, no
    /// whitespace, no offsets such as +00:00. Throws FormatException otherwise.
    /// </summary>
    public static DateTimeOffset ParseUtc(string text)
    {
        if (!TryParseUtc(text, out var value))
        {
            throw new FormatException($"'{text}' is not an ISO 8601 UTC timestamp ending with 'Z'.");
        }

        return value;
    }

    public static bool TryParseUtc(string? text, out DateTimeOffset value)
    {
        value = default;
        // 'FFFFFFF' makes the digits optional, so the dangling "." form ("...00.Z") must be rejected explicitly.
        if (text is null || text.EndsWith(".Z", StringComparison.Ordinal))
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                text,
                ParseFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static readonly string[] ParseFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    public static LaunchClock FromText(string text) => new(ParseUtc(text));

    /// <summary>T0 + offset. Negative offsets (before launch) are allowed. The offset is rounded to the nearest tick.</summary>
    public DateTimeOffset ToUtc(double offsetSeconds)
    {
        if (!double.IsFinite(offsetSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(offsetSeconds), offsetSeconds, "Offset must be a finite number.");
        }

        var ticks = Math.Round(offsetSeconds * TimeSpan.TicksPerSecond);
        if (Math.Abs(ticks) >= long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetSeconds), offsetSeconds, "Offset is too large to represent as a time span.");
        }

        try
        {
            return LaunchTimeUtc.AddTicks((long)ticks);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentOutOfRangeException(
                $"T0 {LaunchTimeUtc:O} + offset {offsetSeconds} s is outside the supported date range.", ex);
        }
    }

    /// <summary>KML time text: yyyy-MM-ddTHH:mm:ssZ (milliseconds optional: ...ss.fffZ). Invariant culture. The offset is first rounded to the nearest tick (100 ns), then the output is truncated (not rounded) to seconds/milliseconds.</summary>
    public string Format(double offsetSeconds, bool includeMilliseconds = false) =>
        ToUtc(offsetSeconds).ToString(includeMilliseconds ? MillisecondsFormat : SecondsFormat, CultureInfo.InvariantCulture);
}
