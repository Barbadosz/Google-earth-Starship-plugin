using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Cli.Tests.Attack;

/// <summary>M1 attack pass: hostile mission/trajectory data pushed through the whole pipeline.</summary>
public sealed class PipelineAttackTests : IDisposable
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;

    private readonly AttackWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- trajectory shape

    [Fact]
    public async Task SinglePointTrajectory_BuildsValidKml()
    {
        _ws.WriteMission(AttackWorkspace.Point(0, 25.99, -97.15, 5));

        var (result, entries) = await _ws.BuildAsync();

        Assert.Equal(1, result.PointCount);
        var doc = AttackWorkspace.Doc(entries);
        Assert.Single(doc.Descendants(Gx + "Track").First().Elements(K + "when"));
        Assert.DoesNotContain("NaN", AttackWorkspace.DocText(entries));
    }

    [Fact]
    public async Task EmptyPointList_IsMissionDataException()
    {
        _ws.WriteMission(string.Empty);
        var ex = await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
        Assert.Contains("points", ex.Message);
    }

    [Theory]
    [InlineData(0, 5)]   // duplicate
    [InlineData(5, 4)]   // going backwards
    public async Task DuplicateOrUnsortedTimes_AreMissionDataException_NamingTheFile(double second, double third)
    {
        _ws.WriteMission(string.Join(",", AttackWorkspace.Point(0, 25.99, -97.15, 5), AttackWorkspace.Point(second, 25.99, -97.14, 5),
            AttackWorkspace.Point(third, 25.99, -97.13, 5)));
        var ex = await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
        Assert.Contains(".json", ex.Message);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("1e999")]
    [InlineData("-1e999")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"25.9\"")]
    public async Task NonNumericOrOverflowingLatitude_IsMissionDataException(string lat)
    {
        _ws.WriteMission($"{{ \"t\": 0, \"lat\": {lat}, \"lon\": -97, \"altM\": 5 }}");
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    [Theory]
    [InlineData(90.0000001, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 180.0000001)]
    [InlineData(0, -180.5)]
    [InlineData(0, 360)]
    [InlineData(1e308, 0)]
    public async Task OutOfRangeCoordinates_AreMissionDataException_NamingThePoint(double lat, double lon)
    {
        _ws.WriteMission(AttackWorkspace.Point(0, 0, 0, 0) + "," + AttackWorkspace.Point(1, lat, lon, 0));
        var ex = await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
        Assert.Contains("points[1]", ex.Message);
    }

    [Fact]
    public async Task NegativeAltitudes_AreKeptAsNegativeMetres()
    {
        _ws.WriteMission(AttackWorkspace.Point(0, 31.5, 35.5, -430) + "," + AttackWorkspace.Point(5, 31.5, 35.5001, -400));
        var (_, entries) = await _ws.BuildAsync();
        Assert.Contains("35.500000,31.500000,-430.0", AttackWorkspace.DocText(entries));
    }

    [Fact]
    public async Task ExtremeAltitude_StaysValidNumber()
    {
        _ws.WriteMission(AttackWorkspace.Point(0, 0, 0, 1e15) + "," + AttackWorkspace.Point(5, 0, 0.0001, -1e15));
        var (_, entries) = await _ws.BuildAsync();
        var text = AttackWorkspace.DocText(entries);
        Assert.DoesNotContain("E+", text);
        Assert.DoesNotContain("Infinity", text);
    }

    [Fact]
    public async Task AntimeridianCrossing_LongitudesStayContinuous_AndInRange()
    {
        // 179.9 -> -179.9 is a ~22 km hop over the date line (shortest way), not a 360 degree sweep.
        _ws.WriteMission(AttackWorkspace.Point(0, 10, 179.9, 100) + "," + AttackWorkspace.Point(100, 10, -179.9, 100));
        var mission = await AttackWorkspace.Pipeline().LoadAndPrepareAsync(_ws.Options(), Ct);

        var points = mission.Vehicles[0].FlightPaths[0].Points;
        Assert.True(points.Count > 2);
        Assert.All(points, p => Assert.InRange(p.Longitude, -180.0, 179.999999999));
        for (var i = 1; i < points.Count; i++)
        {
            var delta = Math.Abs(points[i].Longitude - points[i - 1].Longitude);
            delta = Math.Min(delta, 360 - delta);
            Assert.True(delta < 0.25, $"longitude jump {delta} between points {i - 1} and {i}");
        }
    }

    [Fact]
    public async Task Longitude180_IsNormalisedToMinus180_InKml()
    {
        _ws.WriteMission(AttackWorkspace.Point(0, 0, 180, 0) + "," + AttackWorkspace.Point(5, 0, 179.99995, 0));
        var (_, entries) = await _ws.BuildAsync();
        var coords = AttackWorkspace.Doc(entries).Descendants(Gx + "coord").Select(c => c.Value).ToList();
        Assert.All(coords, c => Assert.NotEqual("180.000000", c.Split(' ')[0]));
    }

    [Theory]
    [InlineData(89.9999, 0, 89.9999, 180)]   // over the pole
    [InlineData(90, 0, 90, 100)]             // standing on the pole
    [InlineData(-90, 20, -90, -150)]         // south pole
    [InlineData(0, 0, 0, 180)]               // antipodal on the equator
    [InlineData(10, 20, -10, -160)]          // antipodal
    [InlineData(25, -97, 25, -97)]           // zero ground distance
    public async Task PolesAndAntipodes_NoNaN_NoInfinity_ValidXml(double lat1, double lon1, double lat2, double lon2)
    {
        _ws.WriteMission(AttackWorkspace.Point(0, lat1, lon1, 100) + "," + AttackWorkspace.Point(3600, lat2, lon2, 200));

        var (_, entries) = await _ws.BuildAsync();

        var text = AttackWorkspace.DocText(entries);
        Assert.DoesNotContain("NaN", text);
        Assert.DoesNotContain("Infinity", text);
        var doc = AttackWorkspace.Doc(entries);
        foreach (var angle in doc.Descendants(Gx + "angles").Select(a => a.Value.Split(' ').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray()))
        {
            Assert.InRange(angle[0], 0, 360);
            Assert.InRange(angle[1], 0, 180);
            Assert.InRange(angle[2], -180, 180);
        }
    }

    [Fact]
    public async Task HugeTrajectory_50kPoints_BuildsInReasonableTime()
    {
        var points = string.Join(",", Enumerable.Range(0, 50_000).Select(i =>
            AttackWorkspace.Point(i, 25 + i * 0.00005, -97 + i * 0.00005, 100 + i)));
        _ws.WriteMission(points);

        var sw = Stopwatch.StartNew();
        var (result, _) = await _ws.BuildAsync();

        Assert.Equal(50_000, result.PointCount);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task TooManyDensifiedPoints_ReportedAsMissionDataException_NotUnexpected()
    {
        // 63 years between two points: 200 million 10-second pieces, over the 1 000 000 point safety cap.
        _ws.WriteMission(AttackWorkspace.Point(0, 25, -97, 0) + "," + AttackWorkspace.Point(2e9, 25, -96, 0));
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    // ---------------------------------------------------------------- time handling

    [Fact]
    public async Task SubMillisecondOffsets_DoNotProduceDuplicateWhenValues()
    {
        _ws.WriteMission(AttackWorkspace.Point(0, 25, -97, 0) + "," + AttackWorkspace.Point(0.0004, 25, -97, 1)
            + "," + AttackWorkspace.Point(5, 25, -97.0001, 2));
        var (_, entries) = await _ws.BuildAsync();
        var whens = AttackWorkspace.Doc(entries).Descendants(Gx + "Track").First().Elements(K + "when").Select(w => w.Value).ToList();
        Assert.Equal(whens.Count, whens.Distinct().Count());
    }

    [Fact]
    public async Task FractionalLaunchTime_IsNotLostForWholeSecondOffsets()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        var (_, entries) = await _ws.BuildAsync(new DateTimeOffset(2026, 12, 1, 14, 0, 0, 500, TimeSpan.Zero));
        var first = AttackWorkspace.Doc(entries).Descendants(Gx + "Track").First().Elements(K + "when").First().Value;
        Assert.Equal("2026-12-01T14:00:00.500Z", first);
    }

    [Fact]
    public async Task EventHugeOffset_IsMissionDataException()
    {
        _ws.WriteMission(
            AttackWorkspace.DefaultPoints(),
            events: """[ { "t": 1e20, "type": "Liftoff", "name": "far", "quality": "Simulated" } ]""");
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    [Fact]
    public async Task LaunchTimeAtEndOfCalendar_IsMissionDataException()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        await Assert.ThrowsAsync<MissionDataException>(
            () => _ws.BuildAsync(new DateTimeOffset(9999, 12, 31, 23, 59, 55, TimeSpan.Zero)));
    }

    [Fact]
    public async Task LaunchTimeAtStartOfCalendar_WithNegativeOffsets_IsMissionDataException()
    {
        _ws.WriteMission(AttackWorkspace.Point(-5, 25, -97, 0) + "," + AttackWorkspace.Point(5, 25, -97.0001, 0));
        await Assert.ThrowsAsync<MissionDataException>(
            () => _ws.BuildAsync(new DateTimeOffset(1, 1, 1, 0, 0, 1, TimeSpan.Zero)));
    }

    // ---------------------------------------------------------------- events

    [Fact]
    public async Task Events_OutsideTrajectoryRange_AndWithoutPosition_AreWritten_InTimeOrder()
    {
        _ws.WriteMission(
            AttackWorkspace.DefaultPoints(),
            events: """
            [ { "t": 100000, "type": "SplashdownOrLanding", "name": "late", "quality": "Simulated" },
              { "t": -100000, "type": "Liftoff", "name": "early", "lat": 25.99, "lon": -97.15, "quality": "Simulated" },
              { "t": 3, "type": "MaxQ", "name": "inside", "lat": 25.99, "lon": -97.15, "altM": 50, "quality": "Simulated" } ]
            """);

        var (_, entries) = await _ws.BuildAsync();

        var folder = AttackWorkspace.Doc(entries).Descendants(K + "Folder").First(f => f.Element(K + "name")?.Value == "Mission Events");
        var placemarks = folder.Elements(K + "Placemark").ToList();
        Assert.Equal(["early", "inside", "late"], placemarks.Select(p => p.Element(K + "name")!.Value).ToArray());
        Assert.Null(placemarks[2].Element(K + "Point"));
        Assert.Equal("clampToGround", placemarks[0].Element(K + "Point")!.Element(K + "altitudeMode")!.Value);
    }

    [Fact]
    public async Task HostileText_IsEscaped_AndRoundTrips()
    {
        const string nasty = "a]]>b <script>alert(1)</script> &amp; \"q\" 'x' é ű 日本 😀 ]]> end";
        var json = System.Text.Json.JsonSerializer.Serialize(nasty);
        _ws.WriteMission(
            AttackWorkspace.DefaultPoints(),
            events: $$"""[ { "t": 1, "type": "MaxQ", "name": {{json}}, "description": {{json}}, "quality": "Simulated" } ]""",
            name: nasty,
            description: nasty);

        var (_, entries) = await _ws.BuildAsync();

        var doc = AttackWorkspace.Doc(entries);
        var ev = doc.Descendants(K + "Placemark").First(p => p.Element(K + "TimeStamp") is not null && p.Element(K + "Point") is null);
        Assert.Equal(nasty, ev.Element(K + "name")!.Value);
        // the description is HTML in a CDATA section: no raw markup from the data, text still readable
        var html = ev.Element(K + "description")!.Value;
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", AttackWorkspace.DocText(entries));
    }

    [Fact]
    public async Task ControlCharactersInJsonStrings_NeverReachTheXml()
    {
        _ws.WriteMission(
            AttackWorkspace.DefaultPoints(),
            events: """[ { "t": 1, "type": "MaxQ", "name": "a\u0000b\u0008c\u001fd\ufffe", "description": "x\u0001y", "quality": "Simulated" } ]""",
            name: "m\u0000n");

        var (_, entries) = await _ws.BuildAsync();

        var text = AttackWorkspace.DocText(entries);
        Assert.DoesNotContain(text, c => c < 0x20 && c is not ('\n' or '\r' or '\t'));
        _ = XDocument.Parse(text);
    }

    [Fact]
    public async Task LoneSurrogateInJson_IsMissionDataException_OrCleaned()
    {
        _ws.WriteMission(
            AttackWorkspace.DefaultPoints(),
            events: """[ { "t": 1, "type": "MaxQ", "name": "bad\ud800name", "quality": "Simulated" } ]""");
        try
        {
            var (_, entries) = await _ws.BuildAsync();
            _ = AttackWorkspace.Doc(entries);
        }
        catch (MissionDataException)
        {
            // an explicit data error is fine too
        }
    }

    [Fact]
    public async Task VeryLongTexts_AreAccepted()
    {
        var longText = new string('x', 200_000);
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), name: longText, description: longText);
        var (_, entries) = await _ws.BuildAsync();
        Assert.Contains(longText, AttackWorkspace.Doc(entries).Descendants(K + "name").First().Value);
    }

    // ---------------------------------------------------------------- model paths / assets

    [Theory]
    [InlineData("../../evil.dae")]
    [InlineData("models/../../evil.dae")]
    [InlineData("..\\\\evil.dae")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/notepad.exe")]
    [InlineData("\\\\\\\\server\\\\share\\\\x.dae")]
    [InlineData("models//x.dae")]
    [InlineData("models/./x.dae")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("models/\\u0000x.dae")]
    public async Task ModelPathTraversalAndGarbage_IsMissionDataException(string modelPath)
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), modelPathJson: $"\"{modelPath}\"");
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    [Fact]
    public async Task ModelPathNull_FallsBackToDefaultModel()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), modelPathJson: "null");
        var (_, entries) = await _ws.BuildAsync();
        Assert.Contains("files/starship.dae", entries.Keys);
    }

    [Fact]
    public async Task ModelFileMissing_IsFileNotFound_NamingTheModel()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), modelPathJson: "\"models/does-not-exist.dae\"");
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => _ws.BuildAsync());
        Assert.Contains("does-not-exist.dae", ex.Message);
    }

    [Fact]
    public async Task ModelPathIsADirectory_ReportsAnIoOrDataError()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), modelPathJson: "\"models\"");
        var ex = await Record.ExceptionAsync(() => _ws.BuildAsync());
        Assert.True(ex is IOException or UnauthorizedAccessException or MissionDataException,
            $"unexpected {ex?.GetType().Name}: {ex?.Message}");
    }

    [Fact]
    public async Task ModelPathWithDeviceName_DoesNotHang()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), modelPathJson: "\"models/con\"");
        var build = Task.Run(async () => await Record.ExceptionAsync(() => _ws.BuildAsync()), Ct);
        var finished = await Task.WhenAny(build, Task.Delay(TimeSpan.FromSeconds(20), Ct));
        Assert.Same(build, finished);
        Assert.IsType<FileNotFoundException>(await build);
    }

    [Fact]
    public async Task ModelAndFolderNamesWithSpacesUnicodeAndPercent_HrefsMatchZipEntries()
    {
        var modelDir = Path.Combine(_ws.Assets, "models", "sub dir");
        Directory.CreateDirectory(modelDir);
        File.Copy(Path.Combine(_ws.Assets, "models", "starship.dae"), Path.Combine(modelDir, "my ship é#%20.dae"));
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), modelPathJson: "\"models/sub dir/my ship é#%20.dae\"");

        var (_, entries) = await _ws.BuildAsync();

        var hrefs = AttackWorkspace.Doc(entries).Descendants(K + "href").Select(h => Uri.UnescapeDataString(h.Value))
            .Where(h => !h.StartsWith("http", StringComparison.Ordinal)).ToList();
        Assert.Contains("files/my ship é#%20.dae", hrefs);
        Assert.All(hrefs, h => Assert.True(entries.ContainsKey(h), $"no KMZ entry for href '{h}'"));
    }

    [Fact]
    public async Task RunAsync_UnicodeAndSpaceFolders_WritesReadableKmz()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        var (_, path) = await AttackWorkspace.Pipeline().RunAsync(_ws.Options(AttackWorkspace.T0), Ct);
        Assert.True(File.Exists(path));
        var entries = AttackWorkspace.ReadZip(await File.ReadAllBytesAsync(path, Ct));
        Assert.Contains("doc.kml", entries.Keys);
        Assert.Empty(Directory.GetFiles(_ws.Output, "*.tmp"));
    }

    // ---------------------------------------------------------------- file / folder state

    [Fact]
    public async Task MissionCodeInFileDiffersFromRequestedCode_IsMissionDataException()
    {
        // data/missions/atk.json says missionCode "other": the output would be atk.kmz built from other-* trajectories.
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), missionCode: "other");
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    [Fact]
    public async Task Utf8Bom_InJson_IsAccepted()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        var path = Path.Combine(_ws.Data, "missions", AttackWorkspace.Code + ".json");
        await File.WriteAllTextAsync(path, await File.ReadAllTextAsync(path, Ct), new UTF8Encoding(true), Ct);
        var (result, _) = await _ws.BuildAsync();
        Assert.Equal(1, result.VehicleCount);
    }

    [Fact]
    public async Task Utf16Json_IsMissionDataException()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        var path = Path.Combine(_ws.Data, "missions", AttackWorkspace.Code + ".json");
        await File.WriteAllTextAsync(path, await File.ReadAllTextAsync(path, Ct), Encoding.Unicode, Ct);
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public async Task MissionFileWithWrongRootContent_IsMissionDataException(string content)
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        await File.WriteAllTextAsync(Path.Combine(_ws.Data, "missions", AttackWorkspace.Code + ".json"), content, Ct);
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    [Fact]
    public async Task MissionFileIsADirectory_IsMissionDataException()
    {
        var path = Path.Combine(_ws.Data, "missions", AttackWorkspace.Code + ".json");
        Directory.CreateDirectory(path);
        await Assert.ThrowsAsync<MissionDataException>(() => _ws.BuildAsync());
    }

    [Fact]
    public async Task OutputDirectoryIsAFile_ThrowsIoException_AndLeavesTheFileAlone()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        await File.WriteAllTextAsync(_ws.Output, "i am a file", Ct);

        await Assert.ThrowsAnyAsync<IOException>(() => AttackWorkspace.Pipeline().RunAsync(_ws.Options(AttackWorkspace.T0), Ct));

        Assert.Equal("i am a file", await File.ReadAllTextAsync(_ws.Output, Ct));
    }

    [Fact]
    public async Task OutputKmzIsADirectory_Throws_AndLeavesNoTempFile()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        Directory.CreateDirectory(Path.Combine(_ws.Output, AttackWorkspace.Code + ".kmz"));

        var ex = await Record.ExceptionAsync(() => AttackWorkspace.Pipeline().RunAsync(_ws.Options(AttackWorkspace.T0), Ct));

        Assert.True(ex is IOException or UnauthorizedAccessException, $"got {ex?.GetType().Name}");
        Assert.Empty(Directory.GetFiles(_ws.Output, "*.tmp"));
    }

    [Fact]
    public async Task OutputKmzLockedByAnotherProgram_ThrowsIo_KeepsOldFile_NoTemp()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        Directory.CreateDirectory(_ws.Output);
        var kmz = Path.Combine(_ws.Output, AttackWorkspace.Code + ".kmz");
        await File.WriteAllTextAsync(kmz, "old", Ct);

        await using (new FileStream(kmz, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var ex = await Record.ExceptionAsync(() => AttackWorkspace.Pipeline().RunAsync(_ws.Options(AttackWorkspace.T0), Ct));
            Assert.True(ex is IOException or UnauthorizedAccessException, $"got {ex?.GetType().Name}");
        }

        Assert.Equal("old", await File.ReadAllTextAsync(kmz, Ct));
        Assert.Empty(Directory.GetFiles(_ws.Output, "*.tmp"));
    }

    [Fact]
    public async Task OutputKmzReadOnly_Throws_KeepsOldFile_NoTemp()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        Directory.CreateDirectory(_ws.Output);
        var kmz = Path.Combine(_ws.Output, AttackWorkspace.Code + ".kmz");
        await File.WriteAllTextAsync(kmz, "old", Ct);
        File.SetAttributes(kmz, FileAttributes.ReadOnly);

        var ex = await Record.ExceptionAsync(() => AttackWorkspace.Pipeline().RunAsync(_ws.Options(AttackWorkspace.T0), Ct));

        Assert.True(ex is IOException or UnauthorizedAccessException, $"got {ex?.GetType().Name}");
        Assert.Equal("old", await File.ReadAllTextAsync(kmz, Ct));
        Assert.Empty(Directory.GetFiles(_ws.Output, "*.tmp"));
    }

    [Fact]
    public async Task AlreadyCancelled_ThrowsOperationCanceled_WritesNothing()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AttackWorkspace.Pipeline().RunAsync(_ws.Options(AttackWorkspace.T0), cts.Token));

        Assert.False(Directory.Exists(_ws.Output));
    }

    // ---------------------------------------------------------------- determinism and the quality principle

    [Fact]
    public async Task Output_IsByteIdentical_AcrossRuns_AndAcrossCultures()
    {
        _ws.WriteMission(
            AttackWorkspace.DefaultPoints() + "," + AttackWorkspace.Point(100.5, 26, -97, 3000),
            events: """[ { "t": 1.5, "type": "MaxQ", "name": "x", "lat": 25.99, "lon": -97.15, "altM": 12.5, "quality": "Simulated" } ]""");

        var baseline = await RunInCultureAsync(CultureInfo.InvariantCulture);
        var again = await RunInCultureAsync(CultureInfo.InvariantCulture);
        Assert.True(baseline.AsSpan().SequenceEqual(again), "two runs differ");

        foreach (var name in new[] { "hu-HU", "de-DE", "fr-FR", "ar-SA", "th-TH", "fa-IR" })
        {
            CultureInfo culture;
            try
            {
                culture = new CultureInfo(name, useUserOverride: false);
            }
            catch (CultureNotFoundException)
            {
                continue;
            }

            var other = await RunInCultureAsync(culture);
            Assert.True(baseline.AsSpan().SequenceEqual(other), $"KMZ differs under culture {name}");
        }
    }

    private async Task<byte[]> RunInCultureAsync(CultureInfo culture)
    {
        var old = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            using var stream = new MemoryStream();
            await AttackWorkspace.Pipeline().BuildAsync(_ws.Options(AttackWorkspace.T0), stream, Ct);
            return stream.ToArray();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = old;
        }
    }

    [Fact]
    public async Task DemoMission_NeverShowsMeasuredOrPublished()
    {
        var options = new StarshipMissionTracker.Cli.Options.CliOptions(
            "demo-ift", AttackWorkspace.T0,
            Path.Combine(AttackWorkspace.RepoRoot(), "data"), Path.Combine(AttackWorkspace.RepoRoot(), "assets"), _ws.Output);
        using var stream = new MemoryStream();
        await AttackWorkspace.Pipeline().BuildAsync(options, stream, Ct);
        var text = AttackWorkspace.DocText(AttackWorkspace.ReadZip(stream.ToArray()));

        Assert.DoesNotContain(">Measured<", text);
        Assert.DoesNotContain(">Published<", text);
        Assert.DoesNotContain("Data quality: Measured", text);
        Assert.DoesNotContain("Data quality: Published", text);
        Assert.DoesNotContain("live telemetry.<br/>Data quality: Measured", text);
    }

    [Fact]
    public async Task PublishedInput_DensifiedPath_IsDowngradedToEstimated_InKml()
    {
        // 2 points 100 s apart -> inserted points. The file claims "Published"; the densified path must not.
        _ws.WriteMission(
            AttackWorkspace.Point(0, 25, -97, 0) + "," + AttackWorkspace.Point(100, 25, -96.5, 0),
            quality: "Published");

        var (_, entries) = await _ws.BuildAsync();

        var doc = AttackWorkspace.Doc(entries);
        var dataQualities = doc.Descendants(K + "Data").Where(d => (string?)d.Attribute("name") == "DataQuality")
            .Select(d => d.Element(K + "value")!.Value).Distinct().ToList();
        Assert.DoesNotContain("Published", dataQualities);
        Assert.DoesNotContain("Measured", dataQualities);
    }

    [Fact]
    public async Task EventQualityDefaultsToMissionQuality_NeverToMeasured()
    {
        _ws.WriteMission(
            AttackWorkspace.DefaultPoints(),
            events: """[ { "t": 1, "type": "MaxQ", "name": "x", "quality": null } ]""");
        var (_, entries) = await _ws.BuildAsync();
        var doc = AttackWorkspace.Doc(entries);
        var ev = doc.Descendants(K + "Placemark").First(p => p.Element(K + "TimeStamp") is not null && p.Element(K + "Point") is null);
        Assert.Equal("Simulated", ev.Descendants(K + "Data").First(d => (string?)d.Attribute("name") == "DataQuality").Element(K + "value")!.Value);
    }

    [Fact]
    public async Task ActualMeasuredPathInPlannedFile_DoesNotCarryTheNotLiveTelemetryClaim()
    {
        // type Actual + quality Measured in a "*-planned.json" file: whatever the data says, the document must not
        // call it "Planned / Reconstructed – not live telemetry" in one place and "Measured" in another.
        _ws.WriteMission(AttackWorkspace.DefaultPoints(), quality: "Measured", trajectoryType: "Actual");
        var (_, entries) = await _ws.BuildAsync();
        var text = AttackWorkspace.DocText(entries);
        Assert.False(
            text.Contains("Data quality: Measured", StringComparison.Ordinal)
            && text.Contains("Planned / Reconstructed – not live telemetry.<br/>Data quality: Measured", StringComparison.Ordinal),
            "mission description says 'Planned / Reconstructed – not live telemetry' right before 'Data quality: Measured'");
    }

    [Fact]
    public async Task DuplicateJsonKey_IsNotSilentlyAccepted()
    {
        // "lat" twice: System.Text.Json keeps the last value without a word unless duplicates are disallowed.
        _ws.WriteMission(
            "{ \"t\": 0, \"lat\": 25.9, \"lat\": 26.1, \"lon\": -97, \"altM\": 5 }");
        var ex = await Record.ExceptionAsync(() => _ws.BuildAsync());
        Assert.IsType<MissionDataException>(ex);
        Assert.Contains("lat", ex.Message, StringComparison.Ordinal);
    }
}
