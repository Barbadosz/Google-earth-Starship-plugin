using System.Net;
using System.Text.RegularExpressions;

namespace StarshipMissionTracker.Api.Tests.EndToEnd;

/// <summary>N-31, part 2: T0 change rules, the planned document's folders and the control page, on the real host.</summary>
public class MissionAndControlPageTests
{
    private const string NewT0 = "2026-12-24T18:00:00Z";
    private const string OldPlannedT0 = "2099-01-01T00:00:00Z";
    private const string LaunchTimePath = "/api/missions/demo-ift/launch-time";
    private const string Body = "{\"launchTimeUtc\":\"" + NewT0 + "\"}";

    private static Task<HttpResponseMessage> PutT0(EndToEndHost host) => host.SendAsync(HttpMethod.Put, LaunchTimePath, Body);

    private static async Task AssertPlannedUnchangedAsync(EndToEndHost host)
    {
        Assert.Equal(OldPlannedT0, (await host.MissionAsync()).GetProperty("plannedLaunchTimeUtc").GetString());
    }

    [Fact]
    public async Task T0Put_IsRejectedWith409_WhileCountdownRunningOrPaused()
    {
        await using var host = EndToEndHost.Start();
        await host.StartSimulationAsync(speed: 10, countdown: 10);

        using (var inCountdown = await PutT0(host))
        {
            Assert.Equal(HttpStatusCode.Conflict, inCountdown.StatusCode);
        }

        await host.AdvanceAsync(10);
        await host.AdvanceAsync(5);
        Assert.Equal("Running", (await host.StatusAsync()).GetProperty("state").GetString());
        using (var running = await PutT0(host))
        {
            Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
        }

        await host.SendJsonAsync(HttpMethod.Post, "/api/simulation/pause", "{}");
        using (var paused = await PutT0(host))
        {
            Assert.Equal(HttpStatusCode.Conflict, paused.StatusCode);
        }

        await AssertPlannedUnchangedAsync(host);
    }

    [Fact]
    public async Task T0Put_AfterStop_ChangesThePlan_AndResetsTheSimulation()
    {
        await using var host = EndToEndHost.Start();
        await host.StartSimulationAsync(speed: 10, countdown: 10);
        await host.AdvanceAsync(10);
        await host.AdvanceAsync(5);
        await host.SendJsonAsync(HttpMethod.Post, "/api/simulation/stop", "{}");
        Assert.NotEmpty(await host.SamplesAsync());
        var oldPlanned = (await host.KmlAsync("planned")).ToString();
        Assert.DoesNotContain("Launch time (T0): " + NewT0[..10], oldPlanned, StringComparison.Ordinal);

        var changed = await host.SendJsonAsync(HttpMethod.Put, LaunchTimePath, Body);
        Assert.Equal(NewT0, changed.GetProperty("plannedLaunchTimeUtc").GetString());

        var mission = await host.MissionAsync();
        Assert.Equal(NewT0, mission.GetProperty("launchTimeUtc").GetString());
        Assert.Equal(NewT0, mission.GetProperty("plannedLaunchTimeUtc").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, mission.GetProperty("simulationLaunchTimeUtc").ValueKind);
        Assert.Empty(await host.SamplesAsync());

        var planned = (await host.KmlAsync("planned")).ToString();
        Assert.Contains("Launch time (T0): " + NewT0, planned, StringComparison.Ordinal);
        Assert.DoesNotContain(OldPlannedT0, planned, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T0Put_AfterACompletedRun_IsAllowed()
    {
        await using var host = EndToEndHost.Start();
        await host.StartSimulationAsync(speed: 100, countdown: 0);
        await host.AdvanceAsync(100000);
        Assert.Equal("Completed", (await host.StatusAsync()).GetProperty("state").GetString());

        var changed = await host.SendJsonAsync(HttpMethod.Put, LaunchTimePath, Body);
        Assert.Equal(NewT0, changed.GetProperty("plannedLaunchTimeUtc").GetString());
        Assert.Contains("Launch time (T0): " + NewT0, (await host.KmlAsync("planned")).ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"launchTimeUtc\":null}")]
    [InlineData("{\"launchTimeUtc\":123}")]
    [InlineData("{\"launchTimeUtc\":\"\"}")]
    [InlineData("{\"launchTimeUtc\":\"tomorrow\"}")]
    [InlineData("{\"launchTimeUtc\":\"2026-12-24T18:00:00+01:00\"}")]
    [InlineData("{\"launchTimeUtc\":\"2026-12-24\"}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public async Task T0Put_WithBadBody_Is400_AndChangesNothing(string json)
    {
        await using var host = EndToEndHost.Start();

        using var response = await host.SendAsync(HttpMethod.Put, LaunchTimePath, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertPlannedUnchangedAsync(host);
    }

    [Fact]
    public async Task T0Put_UnknownMission_Is404_BadCode_Is400_NonJson_Is415()
    {
        await using var host = EndToEndHost.Start();

        using var unknown = await host.SendAsync(HttpMethod.Put, "/api/missions/no-such-mission/launch-time", Body);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var badCode = await host.SendAsync(HttpMethod.Put, "/api/missions/BAD%20CODE!/launch-time", Body);
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Put, LaunchTimePath)
        {
            Content = new StringContent(Body, System.Text.Encoding.UTF8, "text/plain"),
        };
        using var wrongType = await host.Client.SendAsync(request, EndToEndHost.Ct);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        await AssertPlannedUnchangedAsync(host);
    }

    [Fact]
    public async Task MasterKml_HasALookAtWithTheEffectiveT0_BeforeAndAfterAT0Change()
    {
        await using var host = EndToEndHost.Start();

        async Task AssertLookAtAsync(string expectedT0)
        {
            var master = await host.KmlAsync("master");
            var lookAt = master.Descendants(EndToEndHost.K("LookAt"))
                .FirstOrDefault(l => l.Element(EndToEndHost.G("TimeStamp")) is not null);
            Assert.NotNull(lookAt);
            var when = lookAt.Element(EndToEndHost.G("TimeStamp"))?.Element(EndToEndHost.K("when"))?.Value;
            Assert.Equal(expectedT0, when);
        }

        await AssertLookAtAsync(OldPlannedT0);
        await host.SendJsonAsync(HttpMethod.Put, LaunchTimePath, Body);
        await AssertLookAtAsync(NewT0);
    }

    [Fact]
    public async Task PlannedKml_HasCurtainAndEventLabelFolders_WithStableIds()
    {
        await using var host = EndToEndHost.Start();

        var first = await host.KmlAsync("planned");
        var second = await host.KmlAsync("planned");

        foreach (var doc in new[] { first, second })
        {
            var folders = doc.Descendants(EndToEndHost.K("Folder")).ToList();
            var curtain = Assert.Single(folders, f => (string?)f.Attribute("id") == "curtain");
            Assert.Equal("Flight path curtain", (string?)curtain.Element(EndToEndHost.K("name")));
            Assert.Equal(2, curtain.Elements(EndToEndHost.K("Placemark")).Count());
            var labels = Assert.Single(folders, f => (string?)f.Attribute("id") == "event-labels");
            Assert.Equal("Event labels", (string?)labels.Element(EndToEndHost.K("name")));
        }

        Func<System.Xml.Linq.XDocument, List<string>> ids = d =>
            d.Descendants().Select(e => (string?)e.Attribute("id")).Where(i => i is not null).Select(i => i!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(ids(first), ids(second));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/control")]
    public async Task Control_RedirectsToThePage(string path)
    {
        await using var host = EndToEndHost.Start();

        using var response = await host.Client.GetAsync(path, EndToEndHost.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/control/index.html", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ControlPage_AndItsAssets_AreServedWithCorrectTypes_AndAreOffline()
    {
        await using var host = EndToEndHost.Start();
        var texts = new List<string>();

        foreach (var (path, type) in new[]
        {
            ("/control/index.html", "text/html"),
            ("/control/control.js", "javascript"),
            ("/control/control.css", "text/css"),
        })
        {
            using var response = await host.Client.GetAsync(path, EndToEndHost.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(type, response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
            var text = await response.Content.ReadAsStringAsync(EndToEndHost.Ct);
            Assert.NotEmpty(text);
            texts.Add(text);
        }

        var external = new Regex(@"(?:https?|wss?)://(?!(?:127\.0\.0\.1|localhost|\[::1\])(?:[:/]|$))", RegexOptions.IgnoreCase);
        var protocolRelative = new Regex(@"(?:src|href)\s*=\s*[""']//|url\(\s*[""']?//|@import\s+(?:url\(\s*)?[""']?(?:https?:)?//", RegexOptions.IgnoreCase);
        foreach (var text in texts)
        {
            Assert.DoesNotMatch(external, text);
            Assert.DoesNotMatch(protocolRelative, text);
        }
    }
}
