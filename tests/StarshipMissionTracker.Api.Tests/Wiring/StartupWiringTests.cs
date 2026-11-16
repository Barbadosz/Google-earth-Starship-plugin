using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StarshipMissionTracker.Api.Startup;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Api.Tests.Wiring;

/// <summary>Program.cs wiring on the real host (N-30): startup, static files, KML, start order. End-to-end scenarios are N-31.</summary>
public class StartupWiringTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Startup_MigratesAndImportsMissions_BeforeServing()
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        var json = await client.GetStringAsync("/api/missions", Ct);

        Assert.Contains("\"missionCode\":\"demo-ift\"", json, StringComparison.Ordinal);
        Assert.Equal(1, factory.Services.GetRequiredService<DatabaseStartupService>().CompletedRuns);
        Assert.True(File.Exists(factory.DatabaseFile));
    }

    [Fact]
    public async Task HostedServices_DatabaseStartupIsRegisteredBeforeTheSimulator()
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        var hosted = factory.Services.GetServices<IHostedService>().ToList();
        var database = hosted.FindIndex(s => s is DatabaseStartupService);
        var simulator = hosted.FindIndex(s => s is SimulationHostedService);

        Assert.True(database >= 0 && simulator >= 0, "both hosted services are registered");
        Assert.True(database < simulator, "the database startup must start before the simulation hosted service");
    }

    [Fact]
    public async Task AutoStart_FindsTheImportedMission_SoInitializerRanFirst()
    {
        // With a long countdown no sample is written; a Countdown state proves the mission existed when the simulator started.
        await using var factory = new StarshipAppFactory(new Dictionary<string, string?>
        {
            ["Simulation:AutoStart"] = "true",
            ["Simulation:AutoStartMissionCode"] = "demo-ift",
            ["Simulation:AutoStartCountdownSeconds"] = "3600",
        });
        using var client = factory.CreateLocalClient();
        var control = factory.Services.GetRequiredService<ISimulationControl>();

        var status = control.GetStatus();
        for (var i = 0; i < 100 && status.State != SimulationState.Countdown; i++)
        {
            await Task.Delay(50, Ct);
            status = control.GetStatus();
        }

        Assert.Equal(SimulationState.Countdown, status.State);
        Assert.Equal("demo-ift", status.MissionCode);
        Assert.Same(factory.Services.GetRequiredService<SimulationController>(), control);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/control")]
    public async Task Root_AndControl_RedirectToTheControlPage(string path)
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        using var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/control/index.html", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ControlPage_IsServedFromWwwroot()
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        using var page = await client.GetAsync("/control/index.html", Ct);
        using var script = await client.GetAsync("/control/control.js", Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        Assert.Contains("SIMULATED", await page.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }

    [Theory]
    [InlineData("/assets/models/starship.dae", StarshipServer.ColladaMediaType)]
    [InlineData("/assets/icons/starship.png", "image/png")]
    public async Task Assets_AreServedFromTheRepositoryAssetsFolder(string path, string mediaType)
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        using var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task MasterKml_ReturnsANetworkLinkDocument()
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        using var response = await client.GetAsync("/kml/master?mission=demo-ift", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.google-earth.kml+xml", response.Content.Headers.ContentType!.MediaType);
        var kml = XDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        XNamespace ns = "http://www.opengis.net/kml/2.2";
        Assert.Equal(ns + "kml", kml.Root!.Name);
        Assert.NotEmpty(kml.Descendants(ns + "NetworkLink"));
        Assert.Contains("http://127.0.0.1:8080/kml/live?mission=demo-ift", kml.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignHostHeader_IsRejectedByHostFiltering()
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/missions");
        request.Headers.Host = "evil.example";

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
