using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StarshipMissionTracker.Api.Tests.Support;
using StarshipMissionTracker.Api.Tests.Wiring;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Api.Tests.Attack;

/// <summary>
/// M2 attack harness: the real Program.cs host (temp SQLite, manual clock, loopback caller) like EndToEndHost, plus raw
/// requests (own Host header, bytes, content type) and optional extra settings. The hosted tick loop is parked; a test
/// moves time with <see cref="AdvanceAsync"/>.
/// </summary>
public sealed class AttackHost : IAsyncDisposable
{
    public const string Mission = "demo-ift";

    private static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";

    private readonly StarshipAppFactory _factory;

    private AttackHost(StarshipAppFactory factory, ManualTimeProvider clock)
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

    public string DatabaseFile => _factory.DatabaseFile;

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static AttackHost Start(IDictionary<string, string?>? extra = null)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var settings = new Dictionary<string, string?>
        {
            ["Simulation:TickMilliseconds"] = "3600000",
            ["Simulation:SampleIntervalSeconds"] = "1",
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return new AttackHost(new StarshipAppFactory(settings, clock, loopbackCaller: true), clock);
    }

    public async Task AdvanceAsync(double wallSeconds)
    {
        Clock.Advance(TimeSpan.FromSeconds(wallSeconds));
        await Controller.TickAsync(Ct);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json) =>
        SendRawAsync(method, path, json is null ? null : Encoding.UTF8.GetBytes(json), json is null ? null : "application/json", null);

    /// <summary>Raw request: own body bytes, Content-Type text (as sent) and Host header.</summary>
    public Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, byte[]? body, string? contentType, string? host)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentType is not null)
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        if (host is not null)
        {
            request.Headers.TryAddWithoutValidation("Host", host);
        }

        return Client.SendAsync(request, Ct);
    }

    public async Task<(HttpStatusCode Status, string Text)> TextAsync(HttpMethod method, string path, string? json)
    {
        using var response = await SendAsync(method, path, json);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    public async Task<JsonElement> JsonAsync(HttpMethod method, string path, string? json, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var (status, text) = await TextAsync(method, path, json);
        Assert.True(status == expected, $"{method} {path}: expected {expected}, got {(int)status}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public Task<JsonElement> StatusAsync() => JsonAsync(HttpMethod.Get, "/api/simulation", null);

    public Task<JsonElement> StartAsync(double speed, double countdown = 10) =>
        JsonAsync(HttpMethod.Post, "/api/simulation/start",
            string.Create(CultureInfo.InvariantCulture, $"{{\"mission\":\"{Mission}\",\"speed\":{speed},\"countdownSeconds\":{countdown}}}"));

    public Task<HttpResponseMessage> PostAsync(string path) => SendAsync(HttpMethod.Post, path, "{}");

    public Task<JsonElement> MissionAsync() => JsonAsync(HttpMethod.Get, $"/api/missions/{Mission}", null);

    public Task<HttpResponseMessage> PutT0Async(string json) => SendAsync(HttpMethod.Put, $"/api/missions/{Mission}/launch-time", json);

    public async Task<List<JsonElement>> SamplesAsync(string vehicle)
    {
        var json = await JsonAsync(HttpMethod.Get, $"/api/missions/{Mission}/telemetry?vehicle={vehicle}&limit=10000", null);
        return json.EnumerateArray().OrderBy(s => s.GetProperty("offsetSeconds").GetDouble()).ToList();
    }

    public async Task<string> KmlTextAsync(string kind, string query = "")
    {
        using var response = await Client.GetAsync($"/kml/{kind}?mission={Mission}{query}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET /kml/{kind}: {(int)response.StatusCode} {text}");
        return text;
    }

    public async Task<XDocument> KmlAsync(string kind, string query = "") => XDocument.Parse(await KmlTextAsync(kind, query));

    public static XName K(string name) => Kml + name;

    public static double Num(JsonElement e, string name) => e.GetProperty(name).GetDouble();

    public static double Offset(JsonElement status) => Num(status, "missionOffsetSeconds");

    public static string State(JsonElement status) => status.GetProperty("state").GetString()!;

    /// <summary>Fails when a coordinate or numeric element of a KML document is NaN, Infinity or out of range.</summary>
    public static void AssertSaneKml(XDocument doc, string what)
    {
        Assert.Equal(Kml + "kml", doc.Root!.Name);
        foreach (var c in doc.Descendants(Kml + "coordinates"))
        {
            foreach (var tuple in c.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = tuple.Split(',');
                Assert.True(parts.Length is 2 or 3, $"{what}: bad coordinate tuple '{tuple}'");
                var v = parts.Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                Assert.All(v, d => Assert.True(double.IsFinite(d), $"{what}: non-finite coordinate '{tuple}'"));
                Assert.InRange(v[0], -180, 180);
                Assert.InRange(v[1], -90, 90);
            }
        }

        foreach (var name in new[] { "longitude", "latitude", "altitude", "heading", "tilt", "roll", "range", "scale", "refreshInterval", "minRefreshPeriod" })
        {
            foreach (var e in doc.Descendants(Kml + name))
            {
                Assert.True(
                    double.TryParse(e.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d),
                    $"{what}: <{name}> is '{e.Value}'");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}
