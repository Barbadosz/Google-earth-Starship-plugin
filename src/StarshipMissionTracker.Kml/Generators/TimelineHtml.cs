using System.Text;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// The T+ events table for the Mission Information balloon (N-39a). Plain table with inline CSS only (no JS, the
/// Google Earth balloon is a limited HTML subset). All text is HTML-escaped; the order is deterministic
/// (by offset, then name, then source order). The result is meant for <see cref="KmlXml.Cdata"/>.
/// </summary>
internal static class TimelineHtml
{
    public const string Heading = "Flight timeline";

    /// <summary>
    /// Builds the table HTML ("T+hh:mm:ss", event name, optional description under the name).
    /// Returns an empty string when there are no events.
    /// </summary>
    public static string Create(IEnumerable<MissionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var ordered = events
            .Select((e, index) => (Event: e, Index: index))
            .OrderBy(x => x.Event.OffsetSeconds)
            .ThenBy(x => x.Event.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Index)
            .Select(x => x.Event)
            .ToList();
        if (ordered.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("<p><b>").Append(Html(Heading)).Append("</b></p>");
        sb.Append("<table style=\"border-collapse:collapse\">");
        foreach (var e in ordered)
        {
            sb.Append("<tr><td style=\"padding:1px 8px 1px 0;white-space:nowrap;vertical-align:top\"><b>")
                .Append(Html(TLabel(e.OffsetSeconds)))
                .Append("</b></td><td style=\"padding:1px 0\">")
                .Append(Html(e.Name));
            if (!string.IsNullOrWhiteSpace(e.Description))
            {
                sb.Append("<br/><span style=\"color:#555555\">").Append(Html(e.Description)).Append("</span>");
            }

            sb.Append("</td></tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }
}
