namespace StarshipMissionTracker.Api.Startup;

/// <summary>
/// Finds the application content root (the folder with <c>appsettings.json</c> and <c>wwwroot/control/index.html</c>)
/// independently of the working directory: a published build has both next to the dll, a build output
/// (<c>bin/Debug/netX</c>) only has appsettings, so the search walks up to the project folder.
/// </summary>
public static class ContentRootLocator
{
    public const string ControlPageRelativePath = "wwwroot/control/index.html";

    private const int MaxLevelsUp = 6;

    /// <summary>The folder found, or null when none of the start folder and its ancestors qualifies.</summary>
    public static string? Find(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var level = 0; directory is not null && level <= MaxLevelsUp; level++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "appsettings.json"))
                && File.Exists(Path.Combine(directory.FullName, ControlPageRelativePath)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>Null when the control page exists under <paramref name="webRootPath"/>, else the reason (fail fast at startup).</summary>
    public static string? CheckControlPage(string? webRootPath)
    {
        if (string.IsNullOrEmpty(webRootPath) || !File.Exists(Path.Combine(webRootPath, "control", "index.html")))
        {
            return $"Control page not found: wwwroot/control/index.html is missing under '{webRootPath ?? "(no wwwroot)"}'. "
                + "Start the server from its build/publish output or the Api project folder, or pass --contentRoot.";
        }

        return null;
    }
}
