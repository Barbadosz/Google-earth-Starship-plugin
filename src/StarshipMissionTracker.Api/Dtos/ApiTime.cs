using System.Globalization;

namespace StarshipMissionTracker.Api.Dtos;

/// <summary>
/// UTC instants in JSON as <c>yyyy-MM-ddTHH:mm:ss[.fffffff]Z</c> (trailing zero fraction digits dropped), the same form
/// <see cref="Core.Models.LaunchClock.TryParseUtc"/> accepts, so a value read from the API can be sent back unchanged.
/// </summary>
public static class ApiTime
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public static string ToText(DateTimeOffset utc) => utc.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static string? ToText(DateTimeOffset? utc) => utc is { } value ? ToText(value) : null;
}
