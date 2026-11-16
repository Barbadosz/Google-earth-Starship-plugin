using Microsoft.Extensions.DependencyInjection;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Data;

namespace StarshipMissionTracker.Api.Tests.Wiring;

/// <summary>
/// Regression (N-30 first real run): the real simulator (sampler + controller + hosted service) writing through the real
/// TelemetryRepository into a temp SQLite file. Earlier tests used fakes and missed that the simulator's source name was
/// not the one the repository auto-creates, so every tick was rejected.
/// </summary>
public class SimulationPersistenceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartedSimulation_StoresSamples_WithTheInternalSimulatorSource()
    {
        await using var factory = new StarshipAppFactory(new Dictionary<string, string?>
        {
            ["Simulation:TickMilliseconds"] = "100",
        });
        using var client = factory.CreateLocalClient();
        var control = factory.Services.GetRequiredService<ISimulationControl>();
        var telemetry = factory.Services.GetRequiredService<ITelemetryRepository>();

        await control.StartAsync(new SimulationStartRequest("demo-ift", SimulationSpeedLimits.Max, 0), Ct);

        IReadOnlyList<TelemetrySample> latest = [];
        for (var i = 0; i < 100 && latest.Count == 0; i++)
        {
            await Task.Delay(100, Ct);
            latest = await telemetry.GetLatestAsync("demo-ift", Ct);
        }

        var status = control.GetStatus();
        await control.StopAsync(Ct);

        Assert.NotEmpty(latest);
        Assert.All(latest, s =>
        {
            Assert.Equal(DataSourceNames.InternalSimulator, s.SourceName);
            Assert.Equal(DataQuality.Simulated, s.Quality);
        });
        Assert.NotEqual(SimulationState.Idle, status.State);
    }
}
