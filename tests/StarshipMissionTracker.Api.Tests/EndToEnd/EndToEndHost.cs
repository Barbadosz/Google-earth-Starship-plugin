using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StarshipMissionTracker.Api.Tests.Support;
using StarshipMissionTracker.Api.Tests.Wiring;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Api.Tests.EndToEnd;

/// <summary>
/// The real Program.cs host (temp SQLite, real controller, sampler and repositories) on a manual clock (N-31). The
/// hosted tick loop is parked (a one-hour period on a real timer), so a test moves time with <see cref="AdvanceAsync"/>
/// (clock step + one explicit tick) and no test waits for wall time.
/// </summary>
public sealed class EndToEndHost : IAsyncDisposable
{
    public const string Mission = "demo-ift";
    public const string Local = "http://127.0.0.1:8080";

    private static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";
    private static readonly XNamespace Gx = "http://www.google.com/kml/ext/2.2";

    private readonly StarshipAppFactory _factory;

    private EndToEndHost(StarshipAppFactory factory, ManualTimeProvider clock)
    {
        _factory = factory;
        Clock = clock;
        Client = factory.CreateLocalClient();
        Controller = factory.Services.GetRequiredService<SimulationController>();
    }

    public ManualTimeProvider Clock { get; }

    public HttpClient Client { get; }

    public SimulationController Controller { get; }

    public IServiceProvider Services => _factory.Services;

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static EndToEndHost Start(double sampleIntervalSeconds = 1)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var factory = new StarshipAppFactory(
            new Dictionary<string, string?>
            {
                ["Simulation:TickMilliseconds"] = "3600000",
                ["Simulation:SampleIntervalSeconds"] = sampleIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            clock,
            loopbackCaller: true);
        return new EndToEndHost(factory, clock);
    }

    /// <summary>Moves the clock by <paramref name="wallSeconds"/> and runs one simulator tick.</summary>
    public async Task AdvanceAsync(double wallSeconds)
    {
        Clock.Advance(TimeSpan.FromSeconds(wallSeconds));
        await Controller.TickAsync(Ct);
    }

    public async Task<JsonElement> StatusAsync() => await SendJsonAsync(HttpMethod.Get, "/api/simulation", null);

    public Task<HttpResponseMessage> PostAsync(string path, string json = "{}") => SendAsync(HttpMethod.Post, path, json);

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return Client.SendAsync(request, Ct);
    }

    public async Task<JsonElement> SendJsonAsync(HttpMethod method, string path, string? json, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(method, path, json);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{method} {path}: expected {expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public Task<JsonElement> StartSimulationAsync(double speed, double countdown = 10) =>
        SendJsonAsync(HttpMethod.Post, "/api/simulation/start",
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"mission\":\"{Mission}\",\"speed\":{speed},\"countdownSeconds\":{countdown}}}"));

    public async Task<XDocument> KmlAsync(string kind)
    {
        var text = await Client.GetStringAsync($"/kml/{kind}?mission={Mission}", Ct);
        return XDocument.Parse(text);
    }

    /// <summary>Telemetry samples of a vehicle from the API, oldest first.</summary>
    public async Task<List<JsonElement>> SamplesAsync(string vehicle = "Starship")
    {
        var json = await SendJsonAsync(HttpMethod.Get, $"/api/missions/{Mission}/telemetry?vehicle={vehicle}&limit=10000", null);
        return json.EnumerateArray().OrderBy(s => s.GetProperty("offsetSeconds").GetDouble()).ToList();
    }

    public Task<JsonElement> MissionAsync() => SendJsonAsync(HttpMethod.Get, $"/api/missions/{Mission}", null);

    /// <summary>Number of coordinate tuples in all LineStrings of a document (the history path points).</summary>
    public static int PointCount(XDocument doc) =>
        doc.Descendants(Kml + "LineString").Elements(Kml + "coordinates")
            .Sum(c => c.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);

    /// <summary>Longitude of the live model in the vehicle's folder (null before its first sample).</summary>
    public static double? LiveLongitude(XDocument doc, string vehicleFolder) =>
        doc.Descendants(Kml + "Folder")
            .Where(f => (string?)f.Element(Kml + "name") == vehicleFolder)
            .Descendants(Kml + "Model").Elements(Kml + "Location").Elements(Kml + "longitude")
            .Select(e => (double?)double.Parse(e.Value, System.Globalization.CultureInfo.InvariantCulture))
            .FirstOrDefault();

    public static XName K(string name) => Kml + name;

    public static XName G(string name) => Gx + name;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}
