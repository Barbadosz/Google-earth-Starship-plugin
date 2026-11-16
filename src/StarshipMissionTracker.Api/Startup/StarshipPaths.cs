namespace StarshipMissionTracker.Api.Startup;

/// <summary>The resolved, absolute locations (see <see cref="PathOptions"/>).</summary>
public sealed record StarshipPaths(
    string RootDirectory,
    string DataDirectory,
    string AssetsDirectory,
    string DatabaseFile,
    string LogDirectory)
{
    public string MissionsDirectory => Path.Combine(DataDirectory, "missions");

    public string TrajectoriesDirectory => Path.Combine(DataDirectory, "trajectories");

    public string LogFilePattern => Path.Combine(LogDirectory, "starship-.log");

    /// <summary>SQLite connection string for <see cref="DatabaseFile"/>.</summary>
    public string ConnectionString => new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = DatabaseFile }.ToString();

    /// <summary>
    /// Resolves the configured paths. Throws <see cref="InvalidOperationException"/> (fail fast, with the searched
    /// locations) when no root is configured and none can be found, or when the data or assets folder is missing.
    /// </summary>
    public static StarshipPaths Resolve(PathOptions options, string contentRootPath, string? appBaseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        string root;
        if (!string.IsNullOrWhiteSpace(options.Root))
        {
            root = Path.GetFullPath(options.Root, contentRootPath);
        }
        else
        {
            root = FindRepositoryRoot(contentRootPath)
                   ?? (appBaseDirectory is null ? null : FindRepositoryRoot(appBaseDirectory))
                   ?? throw new InvalidOperationException(
                       $"Cannot find the repository root (a folder with data/missions and assets) above '{contentRootPath}'"
                       + (appBaseDirectory is null ? string.Empty : $" or '{appBaseDirectory}'")
                       + $". Set {PathOptions.SectionName}:{nameof(PathOptions.Root)}.");
        }

        var paths = new StarshipPaths(
            root,
            Full(options.Data, nameof(PathOptions.Data)),
            Full(options.Assets, nameof(PathOptions.Assets)),
            Full(options.Database, nameof(PathOptions.Database)),
            Full(options.Logs, nameof(PathOptions.Logs)));

        if (!Directory.Exists(paths.MissionsDirectory))
        {
            throw new InvalidOperationException($"Mission folder not found: '{paths.MissionsDirectory}' ({PathOptions.SectionName}:{nameof(PathOptions.Data)}).");
        }

        if (!Directory.Exists(paths.AssetsDirectory))
        {
            throw new InvalidOperationException($"Assets folder not found: '{paths.AssetsDirectory}' ({PathOptions.SectionName}:{nameof(PathOptions.Assets)}).");
        }

        return paths;

        string Full(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{PathOptions.SectionName}:{name} must not be empty.");
            }

            return Path.GetFullPath(value, root);
        }
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "missions")) && Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
