using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Api.Tests.Wiring;

namespace StarshipMissionTracker.Api.Tests.Attack;

/// <summary>M2 attack: KML documents under every state, history cap, data model and startup import.</summary>
public class M2KmlDataAttackTests
{
    private static readonly string[] Kinds = ["master", "planned", "history", "live"];

    private static IEnumerable<string> FolderIds(XDocument doc) =>
        doc.Descendants(AttackHost.K("Folder")).Select(f => (string?)f.Attribute("id")).Where(id => id is not null).Select(id => id!);

    private static async Task AssertAllDocumentsSaneAsync(AttackHost host, string state)
    {
        foreach (var kind in Kinds)
        {
            var doc = await host.KmlAsync(kind);
            AttackHost.AssertSaneKml(doc, $"{kind} ({state})");
            var ids = FolderIds(doc).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            var elementIds = doc.Descendants().Select(e => (string?)e.Attribute("id")).Where(id => id is not null).ToList();
            Assert.True(elementIds.Count == elementIds.Distinct().Count(), $"{kind} ({state}): duplicate id attribute");
        }
    }

    [Fact]
    public async Task EveryDocument_IsWellFormedAndFinite_InEveryState()
    {
        await using var host = AttackHost.Start();
        await AssertAllDocumentsSaneAsync(host, "idle, no samples");

        await host.StartAsync(speed: 20, countdown: 30);
        await AssertAllDocumentsSaneAsync(host, "countdown");

        await host.AdvanceAsync(30);
        await AssertAllDocumentsSaneAsync(host, "just lifted off, no sample yet");
        await host.AdvanceAsync(40);
        await AssertAllDocumentsSaneAsync(host, "running");

        await host.PostAsync("/api/simulation/pause");
        await host.AdvanceAsync(4_000);
        await AssertAllDocumentsSaneAsync(host, "paused");

        await host.PostAsync("/api/simulation/resume");
        await host.AdvanceAsync(86_400);
        Assert.Equal("Completed", AttackHost.State(await host.StatusAsync()));
        await AssertAllDocumentsSaneAsync(host, "completed");

        await host.PostAsync("/api/simulation/stop");
        await AssertAllDocumentsSaneAsync(host, "stopped after completion");

        using var put = await host.PutT0Async("{\"launchTimeUtc\":\"2035-05-05T05:05:05Z\"}");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        await AssertAllDocumentsSaneAsync(host, "after T0 change");
    }

    [Fact]
    public async Task FolderIds_AreStableAcrossStatesAndRequests()
    {
        await using var host = AttackHost.Start();
        var planned0 = FolderIds(await host.KmlAsync("planned")).Order().ToList();
        var history0 = FolderIds(await host.KmlAsync("history")).Order().ToList();
        var live0 = FolderIds(await host.KmlAsync("live")).Order().ToList();

        await host.StartAsync(speed: 50, countdown: 0);
        await host.AdvanceAsync(30);
        var plannedRunning = FolderIds(await host.KmlAsync("planned")).Order().ToList();
        var historyRunning = FolderIds(await host.KmlAsync("history")).Order().ToList();
        var liveRunning = FolderIds(await host.KmlAsync("live")).Order().ToList();

        Assert.Contains("curtain", planned0);
        Assert.Contains("event-labels", planned0);
        Assert.Equal(planned0, plannedRunning);
        Assert.Equal(history0, historyRunning);
        Assert.Equal(live0, liveRunning);
    }

    [Fact]
    public async Task Master_LookAtTime_IsTheEffectiveT0_InEveryState()
    {
        await using var host = AttackHost.Start();

        async Task AssertLookAtAsync(string state)
        {
            var effective = DateTimeOffset.Parse((await host.MissionAsync()).GetProperty("launchTimeUtc").GetString()!, CultureInfo.InvariantCulture);
            var doc = await host.KmlAsync("master");
            var when = doc.Descendants(AttackHost.K("LookAt")).Descendants(AttackHost.K("when")).Single().Value;
            Assert.True(
                DateTimeOffset.Parse(when, CultureInfo.InvariantCulture) == effective,
                $"{state}: LookAt when {when} != effective T0 {effective:O}");
        }

        await AssertLookAtAsync("planned");
        await host.StartAsync(speed: 10, countdown: 45);
        await AssertLookAtAsync("countdown (anchor)");
        await host.PostAsync("/api/simulation/stop");
        await AssertLookAtAsync("stopped, no samples (anchor cleared)");
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(10);
        await host.PostAsync("/api/simulation/stop");
        await AssertLookAtAsync("stopped with samples (anchor kept)");
        await host.PutT0Async("{\"launchTimeUtc\":\"2033-03-03T03:03:03.5Z\"}");
        await AssertLookAtAsync("after T0 change");
    }

    [Theory]
    [InlineData("&r=1&r=2")]
    [InlineData("&r=")]
    [InlineData("&r=abc%00")]
    [InlineData("&r=99999999999999999999999")]
    [InlineData("&mission=other")]
    [InlineData("&x=%3C%2Fkml%3E")]
    public async Task ChildDocuments_IgnoreJunkQueryParameters(string extra)
    {
        await using var host = AttackHost.Start();

        foreach (var kind in new[] { "planned", "history", "live" })
        {
            using var response = await host.Client.GetAsync($"/kml/{kind}?mission=demo-ift{extra}", AttackHost.Ct);
            Assert.True(
                (int)response.StatusCode is 200 or 400,
                $"{kind}{extra} gave {(int)response.StatusCode}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                AttackHost.AssertSaneKml(XDocument.Parse(await response.Content.ReadAsStringAsync(AttackHost.Ct)), kind);
            }
        }
    }

    [Theory]
    [InlineData("?mission=")]
    [InlineData("?mission=%20")]
    [InlineData("?mission=demo-ift&mission=demo-ift")]
    [InlineData("?mission=demo-ift%0A")]
    [InlineData("?mission=%FF")]
    [InlineData("")]
    public async Task Kml_MissionParameterOddities_AreHandled(string query)
    {
        await using var host = AttackHost.Start();

        foreach (var kind in Kinds)
        {
            using var response = await host.Client.GetAsync($"/kml/{kind}{query}", AttackHost.Ct);
            var code = (int)response.StatusCode;
            Assert.True(code is 200 or 400 or 404, $"{kind}{query} gave {code}");
            if (code != 200)
            {
                Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Kml_HostHeaderWithMarkupAndAmpersand_NeverReachesTheDocument()
    {
        await using var host = AttackHost.Start();

        using var response = await host.SendRawAsync(HttpMethod.Get, "/kml/master?mission=demo-ift", null, null, "a&b<x>\"'");
        var text = await response.Content.ReadAsStringAsync(AttackHost.Ct);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            XDocument.Parse(text);
            Assert.DoesNotContain("a&amp;b", text, StringComparison.Ordinal);
        }

        Assert.True((int)response.StatusCode is 200 or 400, $"gave {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Kml_GzipBody_IsTheSameWellFormedDocument()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(60);
        var request = new HttpRequestMessage(HttpMethod.Get, "/kml/history?mission=demo-ift");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        using var response = await host.Client.SendAsync(request, AttackHost.Ct);
        await using var gz = new GZipStream(await response.Content.ReadAsStreamAsync(AttackHost.Ct), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var text = await reader.ReadToEndAsync(AttackHost.Ct);

        Assert.Equal("gzip", string.Join(",", response.Content.Headers.ContentEncoding));
        AttackHost.AssertSaneKml(XDocument.Parse(text), "gzip history");
        Assert.False(text.StartsWith('﻿'));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(100)]
    public async Task History_NeverExceedsTheCap_AndKeepsFirstAndLastPoint(int cap)
    {
        await using var host = AttackHost.Start(new Dictionary<string, string?> { ["History:MaxPointsPerVehicle"] = cap.ToString(CultureInfo.InvariantCulture) });
        await host.StartAsync(speed: 100, countdown: 0);
        await host.AdvanceAsync(86_400);
        var starship = await host.SamplesAsync("Starship");

        var doc = await host.KmlAsync("history");

        var lines = doc.Descendants(AttackHost.K("LineString")).Elements(AttackHost.K("coordinates"))
            .Select(c => c.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.InRange(l.Length, 2, cap));
        var starshipLine = lines.OrderByDescending(l => l.Length).First();
        double Lon(string tuple) => double.Parse(tuple.Split(',')[0], CultureInfo.InvariantCulture);
        Assert.Equal(AttackHost.Num(starship[0], "longitude"), Lon(starshipLine[0]), 5);
        Assert.Equal(AttackHost.Num(starship[^1], "longitude"), Lon(starshipLine[^1]), 5);
    }

    [Fact]
    public async Task History_DefaultCap2000_HoldsWhenTheRunHasMoreSamples()
    {
        await using var host = AttackHost.Start(new Dictionary<string, string?> { ["Simulation:SampleIntervalSeconds"] = "0.5" });
        await host.StartAsync(speed: 100, countdown: 0);
        await host.AdvanceAsync(86_400);

        var doc = await host.KmlAsync("history");

        var counts = doc.Descendants(AttackHost.K("LineString")).Elements(AttackHost.K("coordinates"))
            .Select(c => c.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length).ToList();
        Assert.Contains(counts, c => c > 1000);
        Assert.All(counts, c => Assert.True(c <= 2000, $"{c} points"));
    }

    [Fact]
    public async Task DuplicateSamples_AreNotStoredTwice()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(5);
        using var scope = host.Services.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var existing = (await telemetry.GetSamplesAsync("demo-ift", VehicleType.Starship, null, 10, AttackHost.Ct)).First();
        var before = (await host.SamplesAsync("Starship")).Count;

        Exception? error = null;
        try
        {
            await telemetry.AddRangeAsync("demo-ift", [existing, existing], AttackHost.Ct);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        var after = (await host.SamplesAsync("Starship")).Count;
        Assert.True(error is not null || after == before, $"duplicate sample stored: {before} -> {after} samples, no error");
        AttackHost.AssertSaneKml(await host.KmlAsync("history"), "history with a duplicate");
    }

    [Fact]
    public async Task OutOfOrderAndFutureSamples_DoNotBreakLiveOrHistory()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(10);
        using var scope = host.Services.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var template = (await telemetry.GetSamplesAsync("demo-ift", VehicleType.Starship, null, 10, AttackHost.Ct)).First();
        var anchor = DateTimeOffset.Parse((await host.MissionAsync()).GetProperty("launchTimeUtc").GetString()!, CultureInfo.InvariantCulture);

        foreach (var offset in new[] { -3600.0, 1e7, 12.3456789 })
        {
            try
            {
                await telemetry.AddRangeAsync("demo-ift", [new TelemetrySample(template.Vehicle, anchor.AddSeconds(offset), template.ReceivedAtUtc, template.Latitude, template.Longitude, template.AltitudeMeters, template.VelocityMetersPerSecond, template.HeadingDegrees, template.PitchDegrees, template.RollDegrees, template.Quality, template.SourceName)], AttackHost.Ct);
            }
            catch (Exception)
            {
                // rejecting is fine; storing must not break the documents
            }
        }

        AttackHost.AssertSaneKml(await host.KmlAsync("live"), "live");
        AttackHost.AssertSaneKml(await host.KmlAsync("history"), "history");
        var (code, _) = await host.TextAsync(HttpMethod.Get, "/api/missions/demo-ift/telemetry?limit=10000", null);
        Assert.Equal(HttpStatusCode.OK, code);
    }

    [Fact]
    public async Task Restart_KeepsTheDatabase_NoDuplicateMissionData_T0AndSamplesSurvive()
    {
        string dbFile;
        string t0;
        int starshipSamples;
        int eventCount;
        int trajectoryCount;
        await using (var first = AttackHost.Start())
        {
            dbFile = first.DatabaseFile;
            eventCount = (await first.JsonAsync(HttpMethod.Get, "/api/missions/demo-ift/events", null)).GetArrayLength();
            trajectoryCount = (await first.JsonAsync(HttpMethod.Get, "/api/missions/demo-ift/trajectory", null)).GetArrayLength();
            await first.StartAsync(speed: 10, countdown: 0);
            await first.AdvanceAsync(30);
            await first.PostAsync("/api/simulation/stop");
            starshipSamples = (await first.SamplesAsync("Starship")).Count;
            t0 = (await first.MissionAsync()).GetProperty("launchTimeUtc").GetString()!;

            // second host on the same database file while the first is still alive and holds nothing open
            var copy = Path.Combine(Path.GetTempPath(), "starship-attack-" + Guid.NewGuid().ToString("N"), "starship.db");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Copy(dbFile, copy);
            await using var second = AttackHost.Start(new Dictionary<string, string?> { ["Paths:Database"] = copy });
            var missions = await second.JsonAsync(HttpMethod.Get, "/api/missions", null);
            Assert.Single(missions.EnumerateArray());
            Assert.Equal(eventCount, (await second.JsonAsync(HttpMethod.Get, "/api/missions/demo-ift/events", null)).GetArrayLength());
            Assert.Equal(trajectoryCount, (await second.JsonAsync(HttpMethod.Get, "/api/missions/demo-ift/trajectory", null)).GetArrayLength());
            Assert.Equal(t0, (await second.MissionAsync()).GetProperty("launchTimeUtc").GetString());
            Assert.Equal(starshipSamples, (await second.SamplesAsync("Starship")).Count);
            Assert.Equal("Idle", AttackHost.State(await second.StatusAsync()));
            Assert.Equal("Scheduled", (await second.MissionAsync()).GetProperty("status").GetString());
        }
    }

    [Fact(Skip = "M2-ATTACK-3: shutdown or crash mid-run leaves the mission InFlight with an actual launch time and no run")]
    public async Task ServerKilledMidRun_NextStartup_DoesNotShowAnInFlightMissionWithoutARun()
    {
        var copy = Path.Combine(Path.GetTempPath(), "starship-attack-" + Guid.NewGuid().ToString("N"), "starship.db");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        await using (var first = AttackHost.Start())
        {
            await first.StartAsync(speed: 10, countdown: 0);
            await first.AdvanceAsync(30);
            Assert.Equal("InFlight", (await first.MissionAsync()).GetProperty("status").GetString());
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Copy(first.DatabaseFile, copy);
        }

        await using var second = AttackHost.Start(new Dictionary<string, string?> { ["Paths:Database"] = copy });

        var state = AttackHost.State(await second.StatusAsync());
        var mission = await second.MissionAsync();
        Assert.Equal("Idle", state);
        Assert.True(
            mission.GetProperty("status").GetString() != "InFlight",
            "after a crash mid-run the mission stays InFlight (with an actual launch time) although no run exists");
    }

    [Fact]
    public async Task LaunchSite_StaysTheLiftoffEventPosition_InEveryState()
    {
        await using var host = AttackHost.Start();
        var events = await host.JsonAsync(HttpMethod.Get, "/api/missions/demo-ift/events", null);
        var liftoff = events.EnumerateArray().First(e => e.GetProperty("type").GetString() == "Liftoff");
        var lat = AttackHost.Num(liftoff, "latitude");
        var lon = AttackHost.Num(liftoff, "longitude");

        async Task AssertSiteAsync(string state)
        {
            var site = (await host.MissionAsync()).GetProperty("launchSite");
            Assert.True(site.ValueKind == JsonValueKind.Object, $"{state}: launchSite is {site.ValueKind}");
            Assert.Equal(lat, AttackHost.Num(site, "latitude"), 9);
            Assert.Equal(lon, AttackHost.Num(site, "longitude"), 9);
            var list = await host.JsonAsync(HttpMethod.Get, "/api/missions", null);
            Assert.Equal(JsonValueKind.Object, list[0].GetProperty("launchSite").ValueKind);
        }

        await AssertSiteAsync("fresh");
        await host.StartAsync(speed: 100, countdown: 5);
        await AssertSiteAsync("countdown");
        await host.AdvanceAsync(10);
        await AssertSiteAsync("running");
        await host.AdvanceAsync(86_400);
        await AssertSiteAsync("completed");
        await host.PostAsync("/api/simulation/stop");
        await AssertSiteAsync("stopped");
        await host.PutT0Async("{\"launchTimeUtc\":\"2040-01-01T00:00:00Z\"}");
        await AssertSiteAsync("after T0 change");

        var master = await host.KmlAsync("planned");
        var placemark = master.Descendants(AttackHost.K("Folder")).First(f => (string?)f.Element(AttackHost.K("name")) == "Mission Information")
            .Descendants(AttackHost.K("coordinates")).First().Value.Trim().Split(',');
        Assert.Equal(lon, double.Parse(placemark[0], CultureInfo.InvariantCulture), 5);
        Assert.Equal(lat, double.Parse(placemark[1], CultureInfo.InvariantCulture), 5);
    }

    [Fact]
    public async Task T0Change_ClearsSamplesAnchorAndActualLaunch_AlsoAfterACompletedRun_AndSameValueIsHarmless()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 100, countdown: 0);
        await host.AdvanceAsync(86_400);
        Assert.NotEmpty(await host.SamplesAsync("Starship"));

        using var same = await host.PutT0Async(
            "{\"launchTimeUtc\":\"" + (await host.MissionAsync()).GetProperty("plannedLaunchTimeUtc").GetString() + "\"}");

        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        var mission = await host.MissionAsync();
        Assert.Equal(JsonValueKind.Null, mission.GetProperty("simulationLaunchTimeUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, mission.GetProperty("actualLaunchTimeUtc").ValueKind);
        Assert.Equal("Scheduled", mission.GetProperty("status").GetString());
        Assert.Empty(await host.SamplesAsync("Starship"));
        Assert.Empty((await host.KmlAsync("history")).Descendants(AttackHost.K("LineString")));
        Assert.Equal("Completed", AttackHost.State(await host.StatusAsync()));
        var rejected = await host.PostAsync("/api/simulation/pause");
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    [Fact]
    public async Task ControlPage_HasNoExternalReferences_InHtmlJsOrCss()
    {
        await using var host = AttackHost.Start();
        var pattern = new System.Text.RegularExpressions.Regex(
            @"(https?:)?//[A-Za-z0-9\[]|@import|\bsrc\s*=\s*[""']\s*(https?:|//)|url\(\s*[""']?\s*(https?:|//)|\bfetch\(\s*[""']\s*https?:|WebSocket|EventSource|XMLHttpRequest|sendBeacon|importScripts|\beval\(|document\.write",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var path in new[] { "/control/index.html", "/control/control.js", "/control/control.css" })
        {
            var text = await host.Client.GetStringAsync(path, AttackHost.Ct);
            var match = pattern.Match(text);
            Assert.False(match.Success, $"{path}: external or risky reference '{match.Value}'");
        }

    }

    [Fact]
    public async Task ControlPage_ErrorBodies_CarryAReadableReason_ForEveryErrorTheApiCanGive()
    {
        await using var host = AttackHost.Start();
        var cases = new List<(HttpStatusCode Expected, HttpResponseMessage Response)>
        {
            (HttpStatusCode.BadRequest, await host.SendAsync(HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"x y\"}")),
            (HttpStatusCode.BadRequest, await host.SendAsync(HttpMethod.Put, "/api/simulation/speed", "{\"speed\":1000}")),
            (HttpStatusCode.NotFound, await host.SendAsync(HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"nope\"}")),
            (HttpStatusCode.Conflict, await host.SendAsync(HttpMethod.Post, "/api/simulation/pause", "{}")),
            (HttpStatusCode.UnsupportedMediaType, await host.SendRawAsync(HttpMethod.Post, "/api/simulation/stop", "{}"u8.ToArray(), "text/plain", null)),
            (HttpStatusCode.RequestEntityTooLarge, await host.SendAsync(HttpMethod.Post, "/api/simulation/start", new string(' ', 20_000))),
        };

        foreach (var (expected, response) in cases)
        {
            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(AttackHost.Ct);
                Assert.True(response.StatusCode == expected, $"expected {expected}, got {(int)response.StatusCode}: {text}");
                using var json = JsonDocument.Parse(text);
                Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("reason").GetString()), text);
                Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            }
        }
    }
}
