using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Telemetry.Simulation;

/// <summary>
/// One simulation run: virtual clock plus sampler for one mission. Pure and deterministic; the
/// <see cref="SimulationController"/> owns it, serialises access and persists the batches. The wall clock is passed in.
/// </summary>
public sealed class SimulationRunner
{
    private readonly PathReplaySampler _sampler;

    public SimulationRunner(Mission mission, DateTimeOffset wallLaunchUtc, double speed, double sampleIntervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(mission);
        MissionCode = mission.MissionCode;
        Clock = new SimulationClock(wallLaunchUtc, speed);
        MissionClock = new LaunchClock(wallLaunchUtc);
        _sampler = new PathReplaySampler(mission, MissionClock, sampleIntervalSeconds);
    }

    public string MissionCode { get; }

    public SimulationClock Clock { get; }

    /// <summary>Mission axis: T0 = the wall instant of liftoff.</summary>
    public LaunchClock MissionClock { get; }

    public bool IsComplete => _sampler.IsComplete;

    /// <summary>Reads the clock once and returns the samples due (none before liftoff).</summary>
    public IReadOnlyList<TelemetrySample> Advance(DateTimeOffset now)
    {
        if (!Clock.HasLiftedOff(now))
        {
            return [];
        }

        return _sampler.Advance(Clock.OffsetAt(now), now);
    }

    public PathReplaySampler.SamplerState Snapshot() => _sampler.Snapshot();

    public void Restore(PathReplaySampler.SamplerState state) => _sampler.Restore(state);
}
