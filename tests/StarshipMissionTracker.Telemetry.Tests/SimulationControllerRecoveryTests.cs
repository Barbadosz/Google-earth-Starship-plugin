using Microsoft.Extensions.Logging;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Telemetry.Tests.Support;

namespace StarshipMissionTracker.Telemetry.Tests;

/// <summary>N-45: <see cref="SimulationController.RecoverOrphanedMissionsAsync"/> (startup recovery of InFlight missions).</summary>
public class SimulationControllerRecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Anchor = TestData.Start.AddMinutes(5);

    private static TelemetrySample Sample() =>
        new(VehicleType.Starship, TestData.Start.AddSeconds(1), TestData.Start.AddSeconds(1), 25.0, -97.0, 100, null, null, null, null, DataQuality.Simulated, "test");

    private static TestData.Rig InFlightRig(bool simulated = true, bool samples = false)
    {
        var rig = new TestData.Rig(simulated: simulated);
        rig.Missions.StoredStatus = MissionStatus.InFlight;
        rig.Missions.StoredActual = TestData.Start;
        rig.Missions.StoredAnchor = Anchor;
        if (samples)
        {
            rig.Telemetry.Samples.Add(Sample());
        }

        return rig;
    }

    [Fact]
    public async Task OrphanedSimulatedMission_BecomesScheduled_WithoutActualLaunchTime()
    {
        var rig = InFlightRig(samples: true);

        await rig.Controller.RecoverOrphanedMissionsAsync(Ct);

        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StoredStatus);
        Assert.Null(rig.Missions.StoredActual);
    }

    [Fact]
    public async Task WithSamples_AnchorIsKept()
    {
        var rig = InFlightRig(samples: true);

        await rig.Controller.RecoverOrphanedMissionsAsync(Ct);

        Assert.Equal(Anchor, rig.Missions.StoredAnchor);
        Assert.Single(rig.Telemetry.Samples);
    }

    [Fact]
    public async Task WithoutSamples_AnchorIsCleared()
    {
        var rig = InFlightRig(samples: false);

        await rig.Controller.RecoverOrphanedMissionsAsync(Ct);

        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StoredStatus);
        Assert.Null(rig.Missions.StoredAnchor);
    }

    [Fact]
    public async Task NonSimulatedInFlightMission_IsUntouched()
    {
        var rig = InFlightRig(simulated: false);

        await rig.Controller.RecoverOrphanedMissionsAsync(Ct);

        Assert.Equal(MissionStatus.InFlight, rig.Missions.StoredStatus);
        Assert.Equal(TestData.Start, rig.Missions.StoredActual);
        Assert.Empty(rig.Missions.StateCalls);
    }

    [Fact]
    public async Task ScheduledMission_IsUntouched()
    {
        var rig = new TestData.Rig();

        await rig.Controller.RecoverOrphanedMissionsAsync(Ct);

        Assert.Empty(rig.Missions.StateCalls);
    }

    [Fact]
    public async Task OneFailingMission_DoesNotStopTheOthers_AndDoesNotThrow()
    {
        var missions = new MultiMissionRepository(["a", "b", "c"]) { FailFor = "a" };
        var logger = new CollectingLogger<SimulationController>();
        var controller = new SimulationController(
            new FakeScopeFactory(missions, new FakeTelemetryRepository()),
            new ManualTimeProvider(TestData.Start),
            new SimulationOptions(),
            logger);

        await controller.RecoverOrphanedMissionsAsync(Ct);

        Assert.Equal(["b", "c"], missions.Written);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("mission a ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WaitsForTheOperationLock()
    {
        var rig = InFlightRig(samples: true);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var running = rig.Controller.RunWhileInactiveAsync("demo", async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 0;
        }, Ct);
        await entered.Task;

        var recovery = rig.Controller.RecoverOrphanedMissionsAsync(Ct);
        await Task.Delay(100, Ct);
        Assert.False(recovery.IsCompleted);
        Assert.Equal(MissionStatus.InFlight, rig.Missions.StoredStatus);

        release.SetResult();
        await running;
        await recovery;
        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StoredStatus);
    }

    private sealed class MultiMissionRepository(string[] codes) : IMissionRepository
    {
        public string? FailFor { get; init; }
        public List<string> Written { get; } = [];

        public Task<IReadOnlyList<MissionInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MissionInfo>>(
                [.. codes.Select(c => new MissionInfo(
                    Guid.NewGuid(), c, c, null, MissionStatus.InFlight, TestData.Start, TestData.Start, true, TestData.Start))]);

        public Task<bool> SetFlightStateAsync(
            string missionCode,
            MissionStatus status,
            DateTimeOffset? actualLaunchTimeUtc,
            DateTimeOffset? simulationLaunchTimeUtc,
            bool clearActualLaunchTime = false,
            bool clearSimulationLaunchTime = false,
            CancellationToken cancellationToken = default)
        {
            if (missionCode == FailFor)
            {
                throw new InvalidOperationException("simulated database failure");
            }

            Written.Add(missionCode);
            return Task.FromResult(true);
        }

        public Task<MissionInfo?> GetInfoAsync(string missionCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MissionInfo?> GetCurrentInfoAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Mission?> GetPlannedMissionAsync(string missionCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ImportAsync(Mission mission, bool isSimulated, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetPlannedLaunchTimeAsync(string missionCode, DateTimeOffset plannedLaunchTimeUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> BeginSimulationRunAsync(string missionCode, DateTimeOffset simulationLaunchTimeUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
