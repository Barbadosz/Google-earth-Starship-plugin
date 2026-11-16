using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Shared LookAt builder (N-40a). With a clock it carries <c>gx:TimeStamp</c> = T0 + offset (UTC, same format as the
/// rest of the module), so Google Earth Pro sets the time slider and the Sun when it flies to the view.
/// Without a clock there is no time element (a refresh then never moves the time slider).
/// The default view values are the ones of the former <c>MissionKmlBuilder.LookAt</c>.
/// </summary>
internal static class MissionLookAt
{
    /// <summary>Distance from the target, metres (same as <see cref="MissionKmlBuilder.InitialViewRangeMeters"/>).</summary>
    public const double DefaultRangeMeters = MissionKmlBuilder.InitialViewRangeMeters;

    /// <summary>Tilt, degrees (0 = straight down).</summary>
    public const double DefaultTiltDegrees = MissionKmlBuilder.InitialViewTiltDegrees;

    /// <summary>Heading, degrees (90 = east).</summary>
    public const double DefaultHeadingDegrees = MissionKmlBuilder.InitialViewHeadingDegrees;

    /// <summary>
    /// Builds a <c>LookAt</c> over a ground point (altitude 0, altitudeMode absolute, as before).
    /// </summary>
    /// <param name="clock">When given, adds <c>gx:TimeStamp</c> = <c>clock.Format(offsetSeconds)</c>; null = no time element.</param>
    /// <param name="offsetSeconds">Offset from T0 of the timestamp (0 = T0); ignored without a clock.</param>
    public static XElement Create(
        double latitude,
        double longitude,
        LaunchClock? clock = null,
        double offsetSeconds = 0,
        double rangeMeters = DefaultRangeMeters,
        double tiltDegrees = DefaultTiltDegrees,
        double headingDegrees = DefaultHeadingDegrees)
    {
        var lookAt = K("LookAt");
        if (clock is not null)
        {
            lookAt.Add(G("TimeStamp", K("when", clock.Format(offsetSeconds))));
        }

        lookAt.Add(
            K("longitude", Degrees(longitude)),
            K("latitude", Degrees(latitude)),
            K("altitude", "0"),
            K("heading", Number(headingDegrees)),
            K("tilt", Number(tiltDegrees)),
            K("range", Number(rangeMeters)),
            K("altitudeMode", "absolute"));
        return lookAt;
    }
}
