using StarshipMissionTracker.Core.Exceptions;

namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// Fully resolved mission: config, the T0 clock and vehicles. Always has a T0, and Config.LaunchTimeUtc always equals
/// Clock.LaunchTimeUtc. At most one vehicle per <see cref="Enums.VehicleType"/>.
/// </summary>
public sealed record Mission
{
    /// <summary>
    /// If the config has a launch time it must equal the clock's T0 (else ArgumentException); a config without one
    /// is replaced by a copy carrying the clock's T0.
    /// </summary>
    public Mission(MissionConfig config, LaunchClock clock, IReadOnlyList<Vehicle> vehicles)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clock);
        var copy = Guard.CopyNoNulls(vehicles, nameof(vehicles));

        if (config.LaunchTimeUtc is { } configT0 && configT0 != clock.LaunchTimeUtc)
        {
            throw new ArgumentException(
                $"Config launch time {configT0:O} differs from the clock T0 {clock.LaunchTimeUtc:O}.", nameof(config));
        }

        var duplicate = copy.GroupBy(v => v.Type).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"More than one vehicle of type {duplicate.Key}.", nameof(vehicles));
        }

        Config = config.LaunchTimeUtc is null ? config.WithLaunchTime(clock.LaunchTimeUtc) : config;
        Clock = clock;
        Vehicles = copy;
    }

    /// <summary>
    /// Resolves T0: override wins, else the config's launch time, else <see cref="MissionDataException"/> (no default T0).
    /// The resolved T0 is stored consistently in Config and Clock.
    /// </summary>
    public static Mission Create(MissionConfig config, DateTimeOffset? launchTimeOverrideUtc, IReadOnlyList<Vehicle> vehicles)
    {
        ArgumentNullException.ThrowIfNull(config);
        var t0 = launchTimeOverrideUtc ?? config.LaunchTimeUtc
            ?? throw new MissionDataException(
                $"Mission '{config.MissionCode}' has no launch time: set launchTimeUtc in the config or pass an override.");
        return new Mission(config.WithLaunchTime(t0), new LaunchClock(t0), vehicles);
    }

    public MissionConfig Config { get; }
    public LaunchClock Clock { get; }
    public IReadOnlyList<Vehicle> Vehicles { get; }
    public string MissionCode => Config.MissionCode;
    public string Name => Config.Name;
}
