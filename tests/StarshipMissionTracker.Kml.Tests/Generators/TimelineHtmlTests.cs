using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests.Generators;

public class TimelineHtmlTests
{
    private static MissionEvent Ev(double offset, string name, string? description = null) =>
        new(offset, EventType.MaxQ, name, description, null, null, null, null, DataQuality.Simulated, "src");

    [Fact]
    public void Empty_ReturnsEmptyString() => Assert.Equal(string.Empty, TimelineHtml.Create([]));

    [Fact]
    public void Rows_AreOrderedByOffset_WithTLabels()
    {
        var html = TimelineHtml.Create([Ev(75, "Max Q"), Ev(-30, "Ignition"), Ev(0, "Liftoff")]);

        var ignition = html.IndexOf("T-00:00:30", StringComparison.Ordinal);
        var liftoff = html.IndexOf("T+00:00:00", StringComparison.Ordinal);
        var maxQ = html.IndexOf("T+00:01:15", StringComparison.Ordinal);
        Assert.True(ignition >= 0 && ignition < liftoff && liftoff < maxQ);
        Assert.Contains("<table", html);
        Assert.Equal(3, html.Split("<tr>").Length - 1);
    }

    [Fact]
    public void Description_IsOptional()
    {
        var html = TimelineHtml.Create([Ev(0, "Liftoff", "Booster lights"), Ev(10, "Other")]);

        Assert.Contains("Booster lights", html);
        Assert.Equal(1, html.Split("<span").Length - 1);
    }

    [Fact]
    public void Text_IsHtmlEscaped()
    {
        var html = TimelineHtml.Create([Ev(0, "<script>alert(1)</script>", "a & b <i>")]);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("a &amp; b &lt;i&gt;", html);
    }

    [Fact]
    public void Output_IsDeterministic_ForEqualOffsets()
    {
        var a = TimelineHtml.Create([Ev(5, "B"), Ev(5, "A")]);
        var b = TimelineHtml.Create([Ev(5, "A"), Ev(5, "B")]);

        Assert.Equal(a, b);
        Assert.True(a.IndexOf(">A<", StringComparison.Ordinal) < a.IndexOf(">B<", StringComparison.Ordinal));
    }
}
