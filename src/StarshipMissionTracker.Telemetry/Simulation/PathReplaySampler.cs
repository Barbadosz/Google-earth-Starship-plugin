using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;
using StarshipMissionTracker.Trajectory.Interpolation;

namespace StarshipMissionTracker.Telemetry.Simulation;

/// <summary>
/// Replays the planned path of every vehicle at the mission offsets <c>n * interval</c>: every offset exactly once and in
/// order, whatever the tick pattern or speed history. Each vehicle ends with one final sample at its last planned point.
/// Samples are labelled <see cref="DataQuality.Simulated"/>; nothing is invented (no noise, velocity null when unknown).
/// </summary>
public sealed class PathReplaySampler
{
    /// <summary>
    /// Full source name of every simulated sample (<see cref="SimulatorSource.Name"/>, the same constant the Data module uses): the telemetry
    /// repository auto-creates only that source and rejects any other unregistered name (provenance rule).
    /// </summary>
    public const string SourceName = SimulatorSource.Name;

    /// <summary>Tolerance (mission seconds) for deciding that a grid offset coincides with the last planned point.</summary>
    private const double Epsilon = 1e-9;

    private readonly LaunchClock _clock;
    private readonly double _intervalSeconds;
    private readonly List<Track> _tracks = [];
    private long[] _next;
    private bool[] _finished;

    public PathReplaySampler(Mission mission, LaunchClock clock, double intervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(clock);
        if (!double.IsFinite(intervalSeconds) || intervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), intervalSeconds, "Interval must be a finite number > 0.");
        }

        _clock = clock;
        _intervalSeconds = intervalSeconds;
        foreach (var vehicle in mission.Vehicles)
        {
            var path = vehicle.FlightPaths.FirstOrDefault(p => p.Type == TrajectoryType.Planned);
            if (path is null || path.Points.Count == 0)
            {
                continue;
            }

            var source = path.Points;
            _tracks.Add(new Track(vehicle.Type, source, OrientationCalculator.FillMissing(source), SourceName));
        }

        _next = _tracks.Select(t => (long)Math.Ceiling(t.Source[0].OffsetSeconds / intervalSeconds - Epsilon)).ToArray();
        _finished = new bool[_tracks.Count];
    }

    /// <summary>True when every vehicle has emitted its final sample (also true without any planned path).</summary>
    public bool IsComplete => _finished.All(f => f);

    /// <summary>All samples due up to <paramref name="offsetSeconds"/> not yet emitted, ordered by vehicle then offset.</summary>
    public IReadOnlyList<TelemetrySample> Advance(double offsetSeconds, DateTimeOffset receivedAtUtc)
    {
        var result = new List<TelemetrySample>();
        if (!double.IsFinite(offsetSeconds))
        {
            return result;
        }

        for (var i = 0; i < _tracks.Count; i++)
        {
            var track = _tracks[i];
            var last = track.Source[^1].OffsetSeconds;
            while (!_finished[i])
            {
                var candidate = _next[i] * _intervalSeconds;
                if (candidate <= last + Epsilon)
                {
                    // A candidate within Epsilon of the last point IS the last point (fractional intervals: n * 0.1 is inexact).
                    var isFinal = candidate >= last - Epsilon;
                    var at = isFinal ? last : candidate;
                    if (at > offsetSeconds)
                    {
                        break;
                    }

                    result.Add(Build(track, at, receivedAtUtc));
                    _next[i]++;
                    _finished[i] = isFinal;
                }
                else
                {
                    if (offsetSeconds < last)
                    {
                        break;
                    }

                    result.Add(Build(track, last, receivedAtUtc));
                    _finished[i] = true;
                }
            }
        }

        return result;
    }

    /// <summary>Copy of the emission state, to roll back when persisting a batch fails.</summary>
    public SamplerState Snapshot() => new((long[])_next.Clone(), (bool[])_finished.Clone());

    public void Restore(SamplerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _next = (long[])state.Next.Clone();
        _finished = (bool[])state.Finished.Clone();
    }

    private TelemetrySample Build(Track track, double offset, DateTimeOffset receivedAtUtc)
    {
        var point = TrajectoryInterpolator.PointAt(track.Source, offset)
                    ?? throw new InvalidOperationException($"No planned point at offset {offset} s for {track.Vehicle}.");
        var oriented = TrajectoryInterpolator.PointAt(track.Oriented, offset) ?? point;

        return new TelemetrySample(
            track.Vehicle,
            _clock.ToUtc(offset),
            receivedAtUtc,
            point.Latitude,
            point.Longitude,
            point.AltitudeMeters,
            point.VelocityMetersPerSecond ?? FiniteDifferenceSpeed(track.Source, offset),
            point.HeadingDegrees ?? oriented.HeadingDegrees,
            point.PitchDegrees ?? oriented.PitchDegrees,
            point.RollDegrees,
            DataQuality.Simulated,
            track.SourceName);
    }

    private static double? FiniteDifferenceSpeed(IReadOnlyList<TrajectoryPoint> points, double offset)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var i = 0;
        while (i < points.Count - 2 && points[i + 1].OffsetSeconds <= offset)
        {
            i++;
        }

        var a = points[i];
        var b = points[i + 1];
        var ground = GeodesicCalculator.Inverse(a.Latitude, a.Longitude, b.Latitude, b.Longitude).DistanceMeters;
        var vertical = b.AltitudeMeters - a.AltitudeMeters;
        return Math.Sqrt((ground * ground) + (vertical * vertical)) / (b.OffsetSeconds - a.OffsetSeconds);
    }

    /// <summary>Opaque emission state (see <see cref="Snapshot"/>).</summary>
    public sealed record SamplerState(long[] Next, bool[] Finished);

    private sealed record Track(
        VehicleType Vehicle,
        IReadOnlyList<TrajectoryPoint> Source,
        IReadOnlyList<TrajectoryPoint> Oriented,
        string SourceName);
}
