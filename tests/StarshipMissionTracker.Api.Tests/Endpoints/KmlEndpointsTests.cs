using System.Net;
using System.Text.RegularExpressions;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Tests.Support;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Tests.Endpoints;

public partial class KmlEndpointsTests
{
    [GeneratedRegex("<cookie>r=(\\d+)</cookie>")]
    private static partial Regex CookieRegex();

    private static async Task<TestApp> StartWithMissionAsync(MissionInfo? info = null)
    {
        var app = await TestApp.StartAsync();
        app.Missions.Put(info ?? TestData.Info());
        return app;
    }

    private static Task<HttpResponseMessage> GetAsync(TestApp app, string path, string? host = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        return app.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static void AssertNoCache(HttpResponseMessage response)
    {
        // HttpClient re-orders the directives when parsing, so compare them, not the raw string.
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.NoStore && cache.NoCache && cache.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, cache.MaxAge);
        Assert.Equal(4, KmlResults.CacheControlValue.Split(',').Length);
        Assert.Equal("no-cache", Assert.Single(response.Headers.GetValues("Pragma")));
        Assert.Equal("0", Assert.Single(response.Content.Headers.GetValues("Expires")));
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
    }

    [Theory]
    [InlineData("/kml/master?mission=demo-ift")]
    [InlineData("/kml/planned?mission=demo-ift")]
    [InlineData("/kml/history?mission=demo-ift")]
    [InlineData("/kml/live?mission=demo-ift")]
    public async Task Get_KnownMission_ReturnsKmlWithNoCacheHeaders(string path)
    {
        await using var app = await StartWithMissionAsync();

        using var response = await GetAsync(app, path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.google-earth.kml+xml", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        AssertNoCache(response);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("<?xml", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_UrlsComeFromRequestHost()
    {
        await using var app = await StartWithMissionAsync();

        using var response = await GetAsync(app, "/kml/master?mission=demo-ift", host: "localhost:9123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:9123/kml/planned?mission=demo-ift", app.Kml.MasterUrls!.PlannedUrl);
        Assert.Equal("http://localhost:9123/kml/history?mission=demo-ift", app.Kml.MasterUrls.HistoryUrl);
        Assert.Equal("http://localhost:9123/kml/live?mission=demo-ift", app.Kml.MasterUrls.LiveUrl);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("http://localhost:9123/kml/live?mission=demo-ift", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_WithoutMission_UsesCurrentMission()
    {
        await using var app = await StartWithMissionAsync();
        app.Missions.Put(TestData.Info("other-1"));
        app.Missions.CurrentCode = "other-1";

        using var response = await GetAsync(app, "/kml/master");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("other-1", app.Kml.LastInfo!.MissionCode);
        Assert.EndsWith("?mission=other-1", app.Kml.MasterUrls!.LiveUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/kml/planned?mission=demo-ift")]
    [InlineData("/kml/history?mission=demo-ift")]
    [InlineData("/kml/live?mission=demo-ift")]
    public async Task ChildDocuments_CookieRevisionIncreasesOnEveryResponse(string path)
    {
        await using var app = await StartWithMissionAsync();

        var first = CookieRegex().Match(await app.GetStringAsync(path));
        // Google Earth appends the cookie to the next request: the extra query parameter must be ignored.
        var second = CookieRegex().Match(await app.GetStringAsync(path + "&r=" + first.Groups[1].Value));

        Assert.True(first.Success && second.Success);
        Assert.True(long.Parse(second.Groups[1].Value) > long.Parse(first.Groups[1].Value));
    }

    [Fact]
    public async Task Revisions_AreSharedAcrossChildDocuments()
    {
        await using var app = await StartWithMissionAsync();

        var planned = long.Parse(CookieRegex().Match(await app.GetStringAsync("/kml/planned?mission=demo-ift")).Groups[1].Value);
        var live = long.Parse(CookieRegex().Match(await app.GetStringAsync("/kml/live?mission=demo-ift")).Groups[1].Value);

        Assert.Equal(planned + 1, live);
    }

    [Theory]
    [InlineData("/kml/planned?mission=nope")]
    [InlineData("/kml/master?mission=nope")]
    [InlineData("/kml/live?mission=nope")]
    public async Task UnknownMission_Returns404PlainTextWithNoCache(string path)
    {
        await using var app = await StartWithMissionAsync();

        using var response = await GetAsync(app, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        AssertNoCache(response);
        Assert.Contains("Unknown mission 'nope'", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMissionAtAll_Returns404()
    {
        await using var app = await TestApp.StartAsync();

        using var response = await GetAsync(app, "/kml/master");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/kml/live?mission=bad%20code")]
    [InlineData("/kml/planned?mission=%3Cx%3E")]
    [InlineData("/kml/master?mission=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidMissionCode_Returns400(string path)
    {
        await using var app = await StartWithMissionAsync();

        using var response = await GetAsync(app, path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Planned_PassesControlUrlAssetsAndMinRefresh()
    {
        await using var app = await StartWithMissionAsync();

        var body = await app.GetStringAsync("/kml/planned?mission=demo-ift");

        Assert.Equal("http://127.0.0.1:8080/control", app.Kml.ControlUrl);
        Assert.Contains("Simulation control: http://127.0.0.1:8080/control", body, StringComparison.Ordinal);
        Assert.Equal("http://127.0.0.1:8080/assets/models", app.Kml.Assets!.ModelBaseUrl);
        Assert.Equal("http://127.0.0.1:8080/assets/icons", app.Kml.Assets.IconBaseUrl);
        Assert.Contains("http://127.0.0.1:8080/assets/icons/starship.png", body, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(2), app.Kml.MinRefresh);
    }

    [Fact]
    public async Task Planned_NonSimulatedMission_HasNoControlUrl()
    {
        await using var app = await StartWithMissionAsync(TestData.Info(isSimulated: false));

        await app.GetStringAsync("/kml/planned?mission=demo-ift");

        Assert.Null(app.Kml.ControlUrl);
    }

    [Fact]
    public async Task Planned_UsesEffectiveT0_AnchorWinsOverPlanned()
    {
        var anchor = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        await using var app = await StartWithMissionAsync(TestData.Info(anchor: anchor));

        await app.GetStringAsync("/kml/planned?mission=demo-ift");

        Assert.Equal(anchor, app.Kml.PlannedMission!.Clock.LaunchTimeUtc);
        Assert.Equal(anchor, app.Kml.LastInfo!.LaunchTimeUtc);
    }

    [Fact]
    public async Task Planned_IsDensified()
    {
        await using var app = await StartWithMissionAsync();

        await app.GetStringAsync("/kml/planned?mission=demo-ift");

        var starship = app.Kml.PlannedMission!.Vehicles.Single(v => v.Type == VehicleType.Starship);
        Assert.True(starship.FlightPaths[0].Points.Count > 3);
    }

    [Fact]
    public async Task PlannedCache_ReusedUntilUpdatedAtChanges()
    {
        await using var app = await StartWithMissionAsync();

        await app.GetStringAsync("/kml/planned?mission=demo-ift");
        await app.GetStringAsync("/kml/planned?mission=demo-ift");
        await app.GetStringAsync("/kml/live?mission=demo-ift");
        Assert.Equal(1, app.Missions.PlannedLoads);

        app.Missions.Put(TestData.Info(updated: TestData.Updated.AddSeconds(1)));
        await app.GetStringAsync("/kml/planned?mission=demo-ift");
        Assert.Equal(2, app.Missions.PlannedLoads);
    }

    [Fact]
    public async Task PlannedCache_RebuiltWhenEffectiveT0ChangesWithSameUpdatedAt()
    {
        await using var app = await StartWithMissionAsync();
        await app.GetStringAsync("/kml/planned?mission=demo-ift");

        var anchor = TestData.PlannedT0.AddHours(-1);
        app.Missions.Put(TestData.Info(anchor: anchor));
        await app.GetStringAsync("/kml/planned?mission=demo-ift");

        Assert.Equal(2, app.Missions.PlannedLoads);
        Assert.Equal(anchor, app.Kml.PlannedMission!.Clock.LaunchTimeUtc);
    }

    [Fact]
    public async Task History_PassesOnlyVehiclesWithActualPath_AndMaxPointsOption()
    {
        await using var app = await TestApp.StartAsync(new Dictionary<string, string?> { ["History:MaxPointsPerVehicle"] = "500" });
        app.Missions.Put(TestData.Info());
        app.Telemetry.History[VehicleType.SuperHeavy] = TestData.ActualPath(60);

        var body = await app.GetStringAsync("/kml/history?mission=demo-ift");

        var vehicle = Assert.Single(app.Kml.HistoryVehicles!);
        Assert.Equal(VehicleType.SuperHeavy, vehicle.Type);
        Assert.Equal(TrajectoryType.Actual, Assert.Single(vehicle.FlightPaths).Type);
        Assert.All(app.Telemetry.HistoryMaxPoints, n => Assert.Equal(500, n));
        Assert.Contains("not measured", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kml_IsGzipCompressedWhenAccepted()
    {
        await using var app = await StartWithMissionAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/kml/planned?mission=demo-ift");
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        using var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        AssertNoCache(response);
    }
}
