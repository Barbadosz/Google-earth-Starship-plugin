using Microsoft.Extensions.Logging.Abstractions;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Telemetry.Tests.Support;

namespace StarshipMissionTracker.Telemetry.Tests;

public class SimulationHostedServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500; i++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10, Ct);
        }

        return condition();
    }

    [Fact]
    public async Task Loop_TicksOnTheTimeProvider_AndWritesSamples()
    {
        var rig = new TestData.Rig();
        using var service = new SimulationHostedService(rig.Controller, rig.Options, rig.Time, NullLogger<SimulationHostedService>.Instance);
        await rig.Start(speed: 1, countdown: 0);
        await service.StartAsync(Ct);

        for (var i = 0; i < 8; i++)
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(rig.Options.TickMilliseconds));
            await Task.Delay(5, Ct);
        }

        Assert.True(await WaitUntil(() => rig.Telemetry.Samples.Count > 0));
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task AutoStart_StartsTheConfiguredMission()
    {
        var rig = new TestData.Rig();
        rig.Options.AutoStart = true;
        rig.Options.AutoStartMissionCode = "demo";
        rig.Options.AutoStartCountdownSeconds = 5;
        using var service = new SimulationHostedService(rig.Controller, rig.Options, rig.Time, NullLogger<SimulationHostedService>.Instance);
        await service.StartAsync(Ct);
        Assert.True(await WaitUntil(() => rig.Controller.GetStatus().State == Core.Enums.SimulationState.Countdown));
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task AutoStart_Failure_IsLogged_NotThrown()
    {
        var rig = new TestData.Rig();
        rig.Options.AutoStart = true;
        rig.Options.AutoStartMissionCode = "does-not-exist";
        var logger = new CollectingLogger<SimulationHostedService>();
        using var service = new SimulationHostedService(rig.Controller, rig.Options, rig.Time, logger);
        await service.StartAsync(Ct);
        Assert.True(await WaitUntil(() => logger.Entries.Any(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error)));
        await service.StopAsync(Ct);
        Assert.Equal(Core.Enums.SimulationState.Idle, rig.Controller.GetStatus().State);
    }
}
