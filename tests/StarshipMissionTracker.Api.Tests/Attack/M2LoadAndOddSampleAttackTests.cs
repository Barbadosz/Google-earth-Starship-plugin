using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Tests.Attack;

/// <summary>M2 attack: odd stored samples and parallel reads while the simulator writes.</summary>
public class M2LoadAndOddSampleAttackTests
{
    private static TelemetrySample Odd(DateTimeOffset at, double? lat, double? lon, double? alt, double? heading = null, double? pitch = null) =>
        new(VehicleType.Starship, at, at, lat, lon, alt, null, heading, pitch, null, DataQuality.Simulated, "Internal simulator");

    [Fact]
    public async Task PositionlessAndPolarLatestSamples_KeepLiveAndHistoryValid()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(10);
        var anchor = DateTimeOffset.Parse((await host.MissionAsync()).GetProperty("launchTimeUtc").GetString()!, CultureInfo.InvariantCulture);
        using var scope = host.Services.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var cases = new (string Name, TelemetrySample Sample)[]
        {
            ("no position", Odd(anchor.AddSeconds(100.5), null, null, null)),
            ("altitude only", Odd(anchor.AddSeconds(101.5), null, null, 1234)),
            ("north pole", Odd(anchor.AddSeconds(102.5), 90, 180, -1e6, 359.999999, 90)),
            ("south pole, huge altitude", Odd(anchor.AddSeconds(103.5), -90, -180, 1e12, 0, -90)),
            ("position, no altitude", Odd(anchor.AddSeconds(104.5), 10, 10, null)),
        };

        foreach (var (name, sample) in cases)
        {
            await telemetry.AddRangeAsync("demo-ift", [sample], AttackHost.Ct);
            AttackHost.AssertSaneKml(await host.KmlAsync("live"), "live after " + name);
            AttackHost.AssertSaneKml(await host.KmlAsync("history"), "history after " + name);
            var (code, _) = await host.TextAsync(HttpMethod.Get, "/api/missions/demo-ift/telemetry?limit=10000", null);
            Assert.Equal(HttpStatusCode.OK, code);
        }
    }

    [Fact]
    public async Task Gzip_QZero_IsNotCompressed()
    {
        await using var host = AttackHost.Start();
        var request = new HttpRequestMessage(HttpMethod.Get, "/kml/live?mission=demo-ift");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip;q=0, identity");

        using var response = await host.Client.SendAsync(request, AttackHost.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ParallelReads_WhileTheSimulatorWrites_NeverFail()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 100, countdown: 0);
        var failures = new List<string>();

        async Task Reader(string path)
        {
            for (var i = 0; i < 25; i++)
            {
                try
                {
                    using var response = await host.Client.GetAsync(path, AttackHost.Ct);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        lock (failures)
                        {
                            failures.Add($"{path}: {(int)response.StatusCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add($"{path}: {ex.GetType().Name} {ex.Message}");
                    }
                }
            }
        }

        async Task Writer()
        {
            for (var i = 0; i < 40; i++)
            {
                try
                {
                    await host.AdvanceAsync(2);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add($"tick: {ex.GetType().Name} {ex.Message}");
                    }
                }
            }
        }

        var work = new List<Task> { Writer() };
        foreach (var path in new[] { "/kml/live?mission=demo-ift", "/kml/history?mission=demo-ift", "/kml/planned?mission=demo-ift",
                     "/api/missions/demo-ift/telemetry?limit=10000", "/api/simulation", "/api/missions/demo-ift" })
        {
            work.Add(Reader(path));
            work.Add(Reader(path));
        }

        await Task.WhenAll(work);

        Assert.Empty(failures.Distinct());
        Assert.Equal("Completed", AttackHost.State(await host.StatusAsync()));
    }
}
