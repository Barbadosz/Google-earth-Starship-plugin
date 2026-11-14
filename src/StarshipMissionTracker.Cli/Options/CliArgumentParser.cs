using System.Text.RegularExpressions;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Cli.Options;

/// <summary>
/// Hand-rolled parser (no extra package): <c>--name value</c> or <c>--name=value</c>, every option at most once,
/// unknown options and stray arguments are errors. The launch time is parsed strictly by <see cref="LaunchClock"/>.
/// </summary>
public static partial class CliArgumentParser
{
    public const string MissionOption = "--mission";
    public const string LaunchTimeOption = "--launch-time";
    public const string DataOption = "--data";
    public const string AssetsOption = "--assets";
    public const string OutputOption = "--output";

    private static readonly string[] ValueOptions = [MissionOption, LaunchTimeOption, DataOption, AssetsOption, OutputOption];

    public static string Usage { get; } =
        """
        Starship Mission Tracker - builds a Google Earth KMZ for a mission.

        Usage:
          StarshipMissionTracker.Cli --mission <code> [--launch-time <T0>] [--data <dir>] [--assets <dir>] [--output <dir>]

        Options:
          --mission <code>       Mission code (required); reads <data>/missions/<code>.json.
          --launch-time <T0>     Launch time override, UTC ISO 8601 with Z, e.g. 2026-12-01T14:00:00Z
                                 (fractional seconds allowed: 2026-12-01T14:00:00.5Z). Wins over launchTimeUtc in the file.
          --data <dir>           Input data folder (default: data).
          --assets <dir>         Models/icons folder (default: assets).
          --output <dir>         Output folder (default: output); the file is <code>.kmz.
          -h, --help             Show this help.

        Relative paths resolve against the current directory.
        Exit codes: 0 ok, 1 data/file error, 2 invalid command line, 130 cancelled (Ctrl+C).
        """;

    /// <summary>Same rule as the mission file's missionCode: the code becomes part of file names.</summary>
    /// <remarks>\z (not $) so a trailing newline fails; Windows reserved device names (CON, NUL, COM1, ...) are rejected.</remarks>
    [GeneratedRegex(@"^(?!(?i:con|prn|aux|nul|com[1-9]|lpt[1-9])\z)[A-Za-z0-9][A-Za-z0-9_-]*\z")]
    private static partial Regex MissionCodePattern();

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help" or "-?" or "/?")
            {
                return CliParseResult.Help();
            }

            var (name, inlineValue) = Split(arg);
            if (!ValueOptions.Contains(name, StringComparer.Ordinal))
            {
                return CliParseResult.Failure(arg.StartsWith('-')
                    ? $"Unknown option '{name}'."
                    : $"Unexpected argument '{arg}'.");
            }

            string value;
            if (inlineValue is not null)
            {
                value = inlineValue;
            }
            else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }
            else
            {
                return CliParseResult.Failure($"Option '{name}' needs a value.");
            }

            if (!values.TryAdd(name, value))
            {
                return CliParseResult.Failure($"Option '{name}' was given more than once.");
            }
        }

        if (!values.TryGetValue(MissionOption, out var code) || string.IsNullOrWhiteSpace(code))
        {
            return CliParseResult.Failure($"Missing required option '{MissionOption} <code>'.");
        }

        if (!MissionCodePattern().IsMatch(code))
        {
            return CliParseResult.Failure(
                $"Invalid mission code '{code}' (allowed: letters, digits, '_' and '-', starting with a letter or digit; Windows device names like CON or NUL are reserved).");
        }

        DateTimeOffset? launchTime = null;
        if (values.TryGetValue(LaunchTimeOption, out var launchText))
        {
            if (!LaunchClock.TryParseUtc(launchText, out var parsed))
            {
                return CliParseResult.Failure(
                    $"Invalid launch time '{launchText}': use UTC ISO 8601 ending in Z, e.g. 2026-12-01T14:00:00Z.");
            }

            launchTime = parsed;
        }

        foreach (var dirOption in new[] { DataOption, AssetsOption, OutputOption })
        {
            if (values.TryGetValue(dirOption, out var dir) && string.IsNullOrWhiteSpace(dir))
            {
                return CliParseResult.Failure($"Option '{dirOption}' needs a non-empty folder path.");
            }
        }

        return CliParseResult.Success(new CliOptions(
            code,
            launchTime,
            values.GetValueOrDefault(DataOption, CliOptions.DefaultDataDirectory),
            values.GetValueOrDefault(AssetsOption, CliOptions.DefaultAssetsDirectory),
            values.GetValueOrDefault(OutputOption, CliOptions.DefaultOutputDirectory)));
    }

    private static (string Name, string? Value) Split(string arg)
    {
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            return (arg, null);
        }

        var eq = arg.IndexOf('=', StringComparison.Ordinal);
        return eq < 0 ? (arg, null) : (arg[..eq], arg[(eq + 1)..]);
    }
}
