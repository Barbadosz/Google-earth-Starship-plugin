namespace StarshipMissionTracker.Api.Tests.Support;

/// <summary>Test clock: <see cref="GetUtcNow"/> only moves with <see cref="Advance"/> / <see cref="Set"/> (timers stay real).</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_gate)
        {
            _now += delta;
        }
    }

    public void Set(DateTimeOffset value)
    {
        lock (_gate)
        {
            _now = value;
        }
    }
}
