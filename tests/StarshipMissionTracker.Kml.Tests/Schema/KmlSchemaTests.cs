using System.Text;
using System.Xml;
using System.Xml.Linq;
using StarshipMissionTracker.Cli.Options;
using StarshipMissionTracker.Cli.Pipeline;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Styles;

namespace StarshipMissionTracker.Kml.Tests.Schema;

/// <summary>
/// N-14: the generated KML must be valid against OGC KML 2.2 (ogckml22.xsd) and the Google gx schema.
/// Element order matters in the XSD (e.g. Placemark children) - that is what these tests catch.
/// </summary>
public class KmlSchemaTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;
    private static readonly DateTimeOffset LaunchOverride = new(2026, 12, 1, 14, 0, 0, TimeSpan.Zero);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (.sln) not found.");
    }

    private static async Task<(Mission Mission, string DocKml)> BuildDemoAsync(DateTimeOffset? launchTime)
    {
        var options = new CliOptions("demo-ift", launchTime,
            Path.Combine(RepoRoot(), "data"), Path.Combine(RepoRoot(), "assets"),
            Path.Combine(Path.GetTempPath(), "unused-output"));
        var pipeline = new MissionKmzPipeline(Serilog.Core.Logger.None);
        var mission = await pipeline.LoadAndPrepareAsync(options, TestContext.Current.CancellationToken);
        return (mission, new MissionKmlBuilder().Build(mission));
    }

    private static string Wrap(params object[] content)
    {
        var doc = new XDocument(
            new XElement(K + "kml", new XAttribute(XNamespace.Xmlns + "gx", Gx.NamespaceName),
                new XElement(K + "Document", content)));
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
        {
            doc.Save(w);
        }

        return sb.ToString();
    }

    private static void AssertValid(string kml)
    {
        var problems = KmlSchemaValidator.Validate(kml);
        if (problems.Count == 0)
        {
            return;
        }

        var lines = kml.Split('\n');
        var report = string.Join(Environment.NewLine, problems.Take(30).Select(p =>
        {
            var src = p.Line > 0 && p.Line <= lines.Length ? lines[p.Line - 1].Trim() : "";
            src = src.Length > 120 ? src[..120] + "..." : src;
            return $"{p} | {src}";
        }));
        Assert.Fail($"{problems.Count} schema problem(s):{Environment.NewLine}{report}");
    }

    // ---- validator sanity (the validator itself must be able to fail) ----

    [Fact]
    public void Validator_MinimalValidDocument_HasNoProblems()
    {
        AssertValid(Wrap(new XElement(K + "name", "x")));
    }

    [Fact]
    public void Validator_WrongElementOrder_IsReported()
    {
        // KML 2.2: Placemark children must be in schema order; Point before name is invalid.
        var placemark = new XElement(K + "Placemark",
            new XElement(K + "Point", new XElement(K + "coordinates", "1,2,3")),
            new XElement(K + "name", "late"));
        Assert.NotEmpty(KmlSchemaValidator.Validate(Wrap(placemark)));
    }

    [Fact]
    public void Validator_UnknownGxElement_IsReportedWhenGxSchemaIsLoaded()
    {
        var kml = Wrap(new XElement(Gx + "NoSuchThing", "x"));
        Assert.NotEmpty(KmlSchemaValidator.Validate(kml, includeGx: true));
    }

    [Fact]
    public void Validator_SchemasAreBundledAndOffline()
    {
        foreach (var f in new[] { "ogckml22.xsd", "atom-author-link.xsd", "xAL.xsd", "kml22gx.xsd" })
        {
            Assert.True(File.Exists(Path.Combine(KmlSchemaValidator.XsdDirectory, f)), f);
        }

        Assert.NotNull(KmlSchemaValidator.CreateSchemaSet(includeGx: true));
    }

    // ---- full document ----

    [Fact]
    public async Task DocKml_DemoMission_ValidatesAgainstKml22AndGx()
    {
        var (_, kml) = await BuildDemoAsync(null);
        AssertValid(kml);
    }

    [Fact]
    public async Task DocKml_DemoMissionWithLaunchTimeOverride_ValidatesAgainstKml22AndGx()
    {
        var (_, kml) = await BuildDemoAsync(LaunchOverride);
        AssertValid(kml);
    }

    // ---- each generator wrapped in a minimal Document ----

    [Fact]
    public void Styles_CreateAll_ValidateInDocument()
    {
        AssertValid(Wrap(KmlStyles.CreateAll()));
    }

    [Fact]
    public async Task PlannedPathGenerator_Placemark_Validates()
    {
        var (mission, _) = await BuildDemoAsync(LaunchOverride);
        foreach (var v in mission.Vehicles)
        {
            foreach (var p in v.FlightPaths)
            {
                AssertValid(Wrap(PlannedPathGenerator.CreatePlacemark(v, p)));
            }
        }
    }

    [Fact]
    public async Task GxTrackGenerator_Placemark_Validates()
    {
        var (mission, _) = await BuildDemoAsync(LaunchOverride);
        foreach (var v in mission.Vehicles)
        {
            foreach (var p in v.FlightPaths)
            {
                AssertValid(Wrap(GxTrackGenerator.CreatePlacemark(v, p, mission.Clock)));
            }
        }
    }

    [Fact]
    public async Task VehicleGenerator_ModelAndIconFolders_Validate()
    {
        var (mission, _) = await BuildDemoAsync(LaunchOverride);
        foreach (var v in mission.Vehicles)
        {
            var path = MissionKmlBuilder.FollowedPath(v);
            AssertValid(Wrap(VehicleGenerator.CreateModelFolder(v, path, mission.Clock)));
            AssertValid(Wrap(VehicleGenerator.CreateIconFolder(v, path, mission.Clock)));
            AssertValid(Wrap(VehicleGenerator.CreateModelPlacemark(v, path, mission.Clock)));
            AssertValid(Wrap(VehicleGenerator.CreateIconPlacemark(v, path, mission.Clock)));
        }
    }

    [Fact]
    public async Task EventGenerator_FolderAndPlacemarks_Validate()
    {
        var (mission, _) = await BuildDemoAsync(LaunchOverride);
        AssertValid(Wrap(EventGenerator.CreateFolder(mission.Config.Events, mission.Clock)));
        foreach (var e in mission.Config.Events)
        {
            AssertValid(Wrap(EventGenerator.CreatePlacemark(e, mission.Clock)));
        }
    }
}
