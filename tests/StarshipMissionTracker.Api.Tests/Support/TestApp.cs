using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarshipMissionTracker.Api.Endpoints;
using StarshipMissionTracker.Api.Services;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Api.Tests.Support;

/// <summary>
/// Minimal in-process host (TestServer) with the Api building blocks and the fakes; no Program.cs. The remote address
/// is loopback unless the request carries <see cref="RemoteIpHeader"/> (value "none" = unknown address).
/// </summary>
public sealed class TestApp : IAsyncDisposable
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";
    public const string Host = "127.0.0.1:8080";

    private readonly WebApplication _app;

    private TestApp(WebApplication app, FakeMissionRepository missions, FakeTelemetryRepository telemetry,
        FakeSimulationControl simulation, RecordingKmlBuilder kml, ManualTimeProvider clock)
    {
        _app = app;
        Missions = missions;
        Telemetry = telemetry;
        Simulation = simulation;
        Kml = kml;
        Clock = clock;
        Client = app.GetTestClient();
        Client.BaseAddress = new Uri("http://" + Host);
    }

    public FakeMissionRepository Missions { get; }
    public FakeTelemetryRepository Telemetry { get; }
    public FakeSimulationControl Simulation { get; }
    public RecordingKmlBuilder Kml { get; }
    public ManualTimeProvider Clock { get; }
    public HttpClient Client { get; }
    public IServiceProvider Services => _app.Services;

    public static async Task<TestApp> StartAsync(IDictionary<string, string?>? configuration = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(configuration ?? new Dictionary<string, string?>());

        var missions = new FakeMissionRepository();
        var telemetry = new FakeTelemetryRepository();
        var simulation = new FakeSimulationControl();
        var kml = new RecordingKmlBuilder();
        var clock = new ManualTimeProvider(TestData.PlannedT0.AddMinutes(5));
        builder.Services.AddSingleton<IMissionRepository>(missions);
        builder.Services.AddSingleton<ITelemetryRepository>(telemetry);
        builder.Services.AddSingleton<ISimulationControl>(simulation);
        builder.Services.AddSingleton<IDynamicKmlBuilder>(kml);
        builder.Services.AddSingleton<TimeProvider>(clock);
        // As the wiring (N-30) does: the simulator options come from the "Simulation" section.
        builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection(SimulationOptions.SectionName));
        builder.Services.AddStarshipApi(builder.Configuration);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            var header = context.Request.Headers[RemoteIpHeader].ToString();
            context.Connection.RemoteIpAddress = header switch
            {
                "" => IPAddress.Loopback,
                "none" => null,
                _ => IPAddress.Parse(header),
            };
            return next(context);
        });
        app.UseResponseCompression();
        app.MapStarshipEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new TestApp(app, missions, telemetry, simulation, kml, clock);
    }

    public static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, HttpContent? content = null, string? remoteIp = null, string? host = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (remoteIp is not null)
        {
            request.Headers.Add(RemoteIpHeader, remoteIp);
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, string body, string? contentType)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return SendAsync(method, path, content);
    }

    public Task<string> GetStringAsync(string path) => Client.GetStringAsync(path, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
