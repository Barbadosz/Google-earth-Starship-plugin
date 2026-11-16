using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace StarshipMissionTracker.Data.Converters;

/// <summary>
/// Stores a DateTimeOffset as its UTC ticks (100 ns, long): ordering and comparison work in SQLite and the fractional second
/// (T0 such as ...:00.1234567Z) survives. Reads return an instant with offset zero.
/// </summary>
public sealed class UtcInstantConverter : ValueConverter<DateTimeOffset, long>
{
    public UtcInstantConverter()
        : base(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {
    }
}
