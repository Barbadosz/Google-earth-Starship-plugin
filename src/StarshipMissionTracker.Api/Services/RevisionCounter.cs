namespace StarshipMissionTracker.Api.Services;

/// <summary>
/// Process-wide revision for the NetworkLinkControl cookie (N-15 layer 2): every child KML response gets a new value,
/// so the URL Google Earth requests next is different each time. Singleton, thread-safe.
/// </summary>
public sealed class RevisionCounter
{
    private long _value;

    /// <summary>The next revision (1, 2, 3, ...).</summary>
    public long Next() => Interlocked.Increment(ref _value);

    /// <summary>The last revision handed out (0 before the first).</summary>
    public long Current => Interlocked.Read(ref _value);
}
