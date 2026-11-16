using System.Globalization;
using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Api.Startup;

/// <summary>
/// Startup validation of the "Simulation" section (review N-26): the same limits the simulator constructors enforce,
/// checked by <c>ValidateOnStart</c> so a bad value fails before any hosted service starts, with a readable message.
/// </summary>
public sealed class SimulationOptionsRules : IValidateOptions<SimulationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SimulationOptions options) =>
        Validate(options) is { } reason ? ValidateOptionsResult.Fail(reason) : ValidateOptionsResult.Success;

    /// <summary>Null when valid, else the reason.</summary>
    public static string? Validate(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        const string s = SimulationOptions.SectionName;

        if (!SimulationSpeedLimits.IsValid(options.DefaultSpeed))
        {
            return Invariant($"{s}:DefaultSpeed must be within [{SimulationSpeedLimits.Min}, {SimulationSpeedLimits.Max}], got {options.DefaultSpeed}.");
        }

        if (!double.IsFinite(options.SampleIntervalSeconds) || options.SampleIntervalSeconds <= 0)
        {
            return Invariant($"{s}:SampleIntervalSeconds must be a finite number > 0, got {options.SampleIntervalSeconds}.");
        }

        if (options.TickMilliseconds < 1)
        {
            return Invariant($"{s}:TickMilliseconds must be >= 1, got {options.TickMilliseconds}.");
        }

        if (options.MaxConsecutiveFailures < 1)
        {
            return Invariant($"{s}:MaxConsecutiveFailures must be >= 1, got {options.MaxConsecutiveFailures}.");
        }

        if (options.AutoStart)
        {
            if (!InputRules.IsValidMissionCode(options.AutoStartMissionCode))
            {
                return $"{s}:AutoStartMissionCode is required when AutoStart is on ({InputRules.MissionCodeRule}), got '{options.AutoStartMissionCode}'.";
            }

            if (!double.IsFinite(options.AutoStartCountdownSeconds)
                || options.AutoStartCountdownSeconds < 0
                || options.AutoStartCountdownSeconds > InputRules.MaxCountdownSeconds)
            {
                return Invariant($"{s}:AutoStartCountdownSeconds must be within [0, {InputRules.MaxCountdownSeconds}], got {options.AutoStartCountdownSeconds}.");
            }
        }

        return null;
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
