using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using StarshipMissionTracker.Cli.Options;
using StarshipMissionTracker.Cli.Pipeline;

namespace StarshipMissionTracker.Cli.Tests.Attack;

/// <summary>
/// Throw-away data/assets/output folders for the attack tests (M1 attack pass). The folder names deliberately contain
/// a space and non-ASCII letters. The mission code is always "atk".
/// </summary>
internal sealed class AttackWorkspace : IDisposable
{
    public const string Code = "atk";
    public static readonly DateTimeOffset T0 = new(2026, 12, 1, 14, 0, 0, TimeSpan.Zero);

    public AttackWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "smt attack éű " + Guid.NewGuid().ToString("N")[..8]);
        Data = Path.Combine(Root, "data dir");
        Assets = Path.Combine(Root, "assets dir");
        Output = Path.Combine(Root, "out dir");
        Directory.CreateDirectory(Path.Combine(Data, "missions"));
        Directory.CreateDirectory(Path.Combine(Data, "trajectories"));
        CopyDirectory(Path.Combine(RepoRoot(), "assets"), Assets);
    }

    public string Root { get; }
    public string Data { get; }
    public string Assets { get; }
    public string Output { get; }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (.sln) not found.");
    }

    public static string Point(double t, double lat, double lon, double alt) =>
        FormattableString.Invariant($"{{ \"t\": {t}, \"lat\": {lat}, \"lon\": {lon}, \"altM\": {alt} }}");

    /// <summary>Ten points one second apart near Starbase: a harmless default path.</summary>
    public static string DefaultPoints() =>
        string.Join(",\n", Enumerable.Range(0, 10).Select(i => Point(i, 25.99, -97.15 + i * 0.0001, 5 + i * 20)));

    /// <summary>Writes a valid mission with one Starship vehicle; every part can be replaced with hostile content.</summary>
    public void WriteMission(
        string points,
        string events = "[]",
        string? modelPathJson = "\"models/starship.dae\"",
        string? launchTimeUtc = "2026-12-01T14:00:00Z",
        string quality = "Simulated",
        string trajectoryType = "Planned",
        string name = "Attack mission",
        string description = "d",
        string? missionCode = null)
    {
        var t0 = launchTimeUtc is null ? string.Empty : $"\"launchTimeUtc\": \"{launchTimeUtc}\",";
        var model = modelPathJson is null ? string.Empty : $", \"modelPath\": {modelPathJson}";
        File.WriteAllText(
            Path.Combine(Data, "missions", Code + ".json"),
            $$"""
            {
              "missionCode": "{{missionCode ?? Code}}",
              "name": {{System.Text.Json.JsonSerializer.Serialize(name)}},
              "description": {{System.Text.Json.JsonSerializer.Serialize(description)}},
              {{t0}}
              "quality": "{{quality}}",
              "source": "attack test",
              "vehicles": [ { "type": "Starship", "name": "Attack ship" {{model}} } ],
              "events": {{events}}
            }
            """);
        WriteTrajectory(points, quality, trajectoryType, missionCode ?? Code);
    }

    public void WriteTrajectory(string points, string quality = "Simulated", string type = "Planned", string? code = null) =>
        File.WriteAllText(
            Path.Combine(Data, "trajectories", (code ?? Code) + "-starship-planned.json"),
            $$"""
            { "vehicle": "Starship", "type": "{{type}}", "quality": "{{quality}}", "source": "attack test",
              "points": [ {{points}} ] }
            """);

    public CliOptions Options(DateTimeOffset? launchTime = null, string? assets = null) =>
        new(Code, launchTime, Data, assets ?? Assets, Output);

    public static MissionKmzPipeline Pipeline() => new(Serilog.Core.Logger.None);

    public async Task<(KmzBuildResult Result, Dictionary<string, byte[]> Entries)> BuildAsync(
        DateTimeOffset? launchTime = null, string? assets = null)
    {
        using var stream = new MemoryStream();
        var result = await Pipeline().BuildAsync(Options(launchTime, assets), stream, TestContext.Current.CancellationToken);
        return (result, ReadZip(stream.ToArray()));
    }

    public static Dictionary<string, byte[]> ReadZip(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            using var copy = new MemoryStream();
            s.CopyTo(copy);
            entries[entry.FullName] = copy.ToArray();
        }

        return entries;
    }

    public static string DocText(Dictionary<string, byte[]> entries) => Encoding.UTF8.GetString(entries["doc.kml"]);

    public static XDocument Doc(Dictionary<string, byte[]> entries) => XDocument.Parse(DocText(entries));

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a leftover temp folder must not fail a test run
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
