namespace StarshipMissionTracker.Api.Startup;

/// <summary>
/// File system locations of the server (section "Paths"). Relative values are resolved against <see cref="Root"/>; an
/// empty <see cref="Root"/> means "find the repository root": the nearest ancestor of the content root (then of the
/// application base directory) that contains both <c>data/missions</c> and <c>assets</c>. No absolute path is configured.
/// </summary>
public sealed class PathOptions
{
    public const string SectionName = "Paths";

    /// <summary>Base directory for the relative paths below; empty = auto-detected repository root.</summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>Folder with <c>missions/*.json</c> and <c>trajectories/*.json</c> (imported at startup).</summary>
    public string Data { get; set; } = "data";

    /// <summary>Folder served at <c>/assets</c> (3D models and icons).</summary>
    public string Assets { get; set; } = "assets";

    /// <summary>SQLite database file (the directory is created on first start).</summary>
    public string Database { get; set; } = "data/starship.db";

    /// <summary>Folder of the daily rolling log file <c>starship-YYYYMMDD.log</c>.</summary>
    public string Logs { get; set; } = "logs";
}

