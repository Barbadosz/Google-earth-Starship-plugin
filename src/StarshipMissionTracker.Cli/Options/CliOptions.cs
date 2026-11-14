namespace StarshipMissionTracker.Cli.Options;

/// <summary>
/// Parsed command line. Directories are as given (relative paths resolve against the current directory).
/// </summary>
/// <param name="MissionCode">Mission code; the mission file is <c>&lt;data&gt;/missions/&lt;code&gt;.json</c>.</param>
/// <param name="LaunchTimeOverrideUtc">Optional T0 override (wins over the mission file's <c>launchTimeUtc</c>).</param>
public sealed record CliOptions(
    string MissionCode,
    DateTimeOffset? LaunchTimeOverrideUtc,
    string DataDirectory = CliOptions.DefaultDataDirectory,
    string AssetsDirectory = CliOptions.DefaultAssetsDirectory,
    string OutputDirectory = CliOptions.DefaultOutputDirectory)
{
    public const string DefaultDataDirectory = "data";
    public const string DefaultAssetsDirectory = "assets";
    public const string DefaultOutputDirectory = "output";

    /// <summary>Output file name: <c>&lt;code&gt;.kmz</c>.</summary>
    public string OutputFileName => MissionCode + ".kmz";
}
