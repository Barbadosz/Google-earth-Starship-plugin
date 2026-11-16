using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Api.Tests.Support;

namespace StarshipMissionTracker.Api.Tests.Endpoints;

/// <summary>DNS rebinding protection: Host check on mutating endpoints and in the KML URLs (review N-29 M1).</summary>
public class HostFilterTests
{
    private const string Hostile = "evil.example";
    private static readonly string[] Defaults = new HostFilterOptions().AllowedHostNames;

    public static TheoryData<string, string, string> MutatingCalls => new()
    {
        { "POST", "/api/simulation/start", "{\"mission\":\"demo-ift\"}" },
        { "POST", "/api/simulation/stop", "{}" },
        { "PUT", "/api/simulation/speed", "{\"speed\":2}" },
        { "POST", "/api/simulation/pause", "{}" },
        { "POST", "/api/simulation/resume", "{}" },
        { "PUT", "/api/missions/demo-ift/launch-time", "{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}" },
    };

    [Theory]
    [MemberData(nameof(MutatingCalls))]
    public async Task Mutating_WithHostileHost_FromLoopback_Returns403(string method, string path, string body)
    {
        await using var app = await TestApp.StartAsync();
        app.Missions.Put(TestData.Info());

        using var response = await app.SendAsync(new HttpMethod(method), path, TestApp.Json(body), host: Hostile + ":8080");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"error\":\"forbidden\"", text, StringComparison.Ordinal);
        Assert.Empty(app.Simulation.Calls);
        Assert.Empty(app.Missions.PlannedEdits);
    }

    [Theory]
    [InlineData("localhost:5000")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1:8080")]
    [InlineData("127.0.0.2:8080")]
    [InlineData("[::1]:8080")]
    public async Task Mutating_WithLocalHost_IsAccepted(string host)
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.SendAsync(HttpMethod.Post, "/api/simulation/stop", TestApp.Json("{}"), host: host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["stop"], app.Simulation.Calls);
    }

    [Fact]
    public async Task ConfiguredHostName_IsAccepted_DefaultsStay()
    {
        await using var app = await TestApp.StartAsync(new Dictionary<string, string?> { ["HostFilter:AllowedHostNames:0"] = "my-pc" });

        using var named = await app.SendAsync(HttpMethod.Post, "/api/simulation/stop", TestApp.Json("{}"), host: "my-pc:8080");
        using var local = await app.SendAsync(HttpMethod.Post, "/api/simulation/stop", TestApp.Json("{}"), host: "localhost:8080");

        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("my pc")]
    public async Task InvalidHostName_FailsAtStartup(string name)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestApp.StartAsync(new Dictionary<string, string?> { ["HostFilter:AllowedHostNames:0"] = name });
        });
    }

    [Fact]
    public async Task Kml_WithHostileHost_UrlsUseLoopbackFallback()
    {
        await using var app = await TestApp.StartAsync();
        app.Missions.Put(TestData.Info());

        using var response = await app.SendAsync(HttpMethod.Get, "/kml/master?mission=demo-ift", host: Hostile + ":8080");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Hostile, body, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("http://127.0.0.1", app.Kml.MasterUrls!.PlannedUrl, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, app.Kml.Assets!.ModelBaseUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("localhost:8080", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.10.0.1:80", true)]
    [InlineData("[::1]", true)]
    [InlineData("[::1]:8080", true)]
    [InlineData("evil.example", false)]
    [InlineData("localhost.evil.example", false)]
    [InlineData("localhost.", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("[::2]", false)]
    [InlineData("", false)]
    public void LocalHostRule_IsAllowed(string host, bool allowed) =>
        Assert.Equal(allowed, LocalHostRule.IsAllowed(new HostString(host), Defaults));

    [Theory]
    [InlineData(9000, "http://127.0.0.1:9000")]
    [InlineData(0, "http://127.0.0.1")]
    public void ServerUrls_HostileHost_FallsBackToLoopback(int localPort, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(Hostile + ":9000");
        context.Connection.LocalPort = localPort;

        var urls = ServerUrls.FromRequest(context.Request, Defaults, out var fallback);

        Assert.True(fallback);
        Assert.Equal(expected, urls.BaseUrl);
    }
}
