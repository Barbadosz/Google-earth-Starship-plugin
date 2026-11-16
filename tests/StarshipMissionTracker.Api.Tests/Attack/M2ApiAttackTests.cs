using System.Net;
using System.Text;

namespace StarshipMissionTracker.Api.Tests.Attack;

/// <summary>M2 attack: HTTP API input, content types, Host rules, odd ids, concurrency (real host, manual clock).</summary>
public class M2ApiAttackTests
{
    private static async Task AssertStillIdleAsync(AttackHost host, string what) =>
        Assert.True(AttackHost.State(await host.StatusAsync()) == "Idle", $"{what}: a rejected start must not start a run");

    [Theory]
    [InlineData("{\"mission\":123}")]
    [InlineData("{\"mission\":null}")]
    [InlineData("{\"mission\":\"demo-ift\\n\"}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":\"10\"}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":1e999}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":true}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":[1]}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":0.0999999999}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":100.0000001}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":-0}")]
    [InlineData("{\"mission\":\"demo-ift\",\"countdownSeconds\":-1}")]
    [InlineData("{\"mission\":\"demo-ift\",\"countdownSeconds\":86400.5}")]
    [InlineData("{\"mission\":\"demo-ift\",\"countdownSeconds\":1e308}")]
    [InlineData("{\"mission\":\"demo-ift\",\"countdownSeconds\":\"5\"}")]
    [InlineData("{\"mission\":\"demo-ift\",\"countdownSeconds\":{}}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("12")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{\"mission\":\"demo-ift\",}")]
    [InlineData("{\"mission\":\"demo-ift\"} trailing")]
    [InlineData("{\"mission\":\"demo-ift\"/*c*/}")]
    [InlineData("{\"mission\":\"demo-ift\",\"mission\":\"demo-ift\"}")]
    [InlineData("{\"mission\":\"demo-ift\",\"mis\\u0073ion\":\"x\"}")]
    [InlineData("{\"a\":[[[[[[[[[[1]]]]]]]]]],\"mission\":\"demo-ift\"}")]
    [InlineData("{\"mission\":\"\\ud800\"}", Skip = "M2-ATTACK-1: lone surrogate in a JSON string gives an unhandled InvalidOperationException (500)")]
    [InlineData("{\"mission\":\"demo\\u0000ift\"}")]
    [InlineData("{\"mission\":\"d\\u00e9mo-ift\"}")]
    [InlineData("{\"mission\":\"\\ud83d\\ude80\"}")]
    public async Task Start_HostileBodies_Return400_Never5xx_AndStartNothing(string body)
    {
        await using var host = AttackHost.Start();

        var (status, text) = await host.TextAsync(HttpMethod.Post, "/api/simulation/start", body);

        Assert.True(status == HttpStatusCode.BadRequest, $"body {body}: expected 400, got {(int)status}: {text}");
        await AssertStillIdleAsync(host, body);
    }

    [Fact(Skip = "M2-ATTACK-1: invalid UTF-8 inside a JSON string gives an unhandled InvalidOperationException (500)")]
    public async Task Start_InvalidUtf8Bytes_Return400()
    {
        await using var host = AttackHost.Start();
        byte[] bytes = [.. "{\"mission\":\"demo-"u8, 0xFF, 0xFE, .. "\"}"u8];

        using var response = await host.SendRawAsync(HttpMethod.Post, "/api/simulation/start", bytes, "application/json", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertStillIdleAsync(host, "invalid utf-8");
    }

    [Fact]
    public async Task Start_Utf8Bom_IsEither200Or400_Never5xx()
    {
        await using var host = AttackHost.Start();
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "{\"mission\":\"demo-ift\"}"u8];

        using var response = await host.SendRawAsync(HttpMethod.Post, "/api/simulation/start", bytes, "application/json", null);

        Assert.True((int)response.StatusCode is 200 or 400, $"BOM body gave {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(100)]
    public async Task Start_SpeedBoundaries_AreAcceptedExactly(double speed)
    {
        await using var host = AttackHost.Start();

        var status = await host.StartAsync(speed);

        Assert.Equal("Countdown", AttackHost.State(status));
        Assert.Equal(speed, AttackHost.Num(status, "speed"));
    }

    [Theory]
    [InlineData("0.09999999999999999")]
    [InlineData("100.00000000000001")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1e-320")]
    [InlineData("1.7976931348623157e308")]
    public async Task PutSpeed_JustOutside_Returns400_AndRunKeepsItsSpeed(string speed)
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(10);

        var (status, text) = await host.TextAsync(HttpMethod.Put, "/api/simulation/speed", $"{{\"speed\":{speed}}}");

        Assert.True(status == HttpStatusCode.BadRequest, $"speed {speed}: {(int)status} {text}");
        Assert.Equal(10, AttackHost.Num(await host.StatusAsync(), "speed"));
    }

    [Theory]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/json,text/html")]
    [InlineData("application/jsonp")]
    [InlineData("application/json-patch+json")]
    [InlineData("text/json")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task Mutating_WeirdContentTypes_Return415_AndChangeNothing(string contentType)
    {
        await using var host = AttackHost.Start();

        using var response = await host.SendRawAsync(
            HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"demo-ift\"}"u8.ToArray(), contentType, null);

        Assert.True(response.StatusCode == HttpStatusCode.UnsupportedMediaType, $"'{contentType}' gave {(int)response.StatusCode}");
        await AssertStillIdleAsync(host, contentType);
    }

    [Fact]
    public async Task Mutating_NoContentTypeAtAll_Returns415()
    {
        await using var host = AttackHost.Start();

        using var response = await host.SendRawAsync(
            HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"demo-ift\"}"u8.ToArray(), null, null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertStillIdleAsync(host, "no content type");
    }

    [Theory]
    [InlineData("APPLICATION/JSON")]
    [InlineData("application/json ; charset=UTF-8")]
    public async Task Mutating_CaseAndSpacingVariantsOfJson_AreAccepted(string contentType)
    {
        await using var host = AttackHost.Start();

        using var response = await host.SendRawAsync(
            HttpMethod.Post, "/api/simulation/stop", "{}"u8.ToArray(), contentType, null);

        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.OK, $"'{contentType}' gave {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData("localhost.")]
    [InlineData("0.0.0.0:8080")]
    [InlineData("127.0.0.1.nip.io")]
    [InlineData("localhost@evil.example")]
    [InlineData("evil.example#127.0.0.1")]
    [InlineData("localhost.evil.example")]
    public async Task Mutating_HostileHostHeaders_Return403_AndChangeNothing(string hostHeader)
    {
        await using var host = AttackHost.Start();

        using var response = await host.SendRawAsync(
            HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"demo-ift\"}"u8.ToArray(), "application/json", hostHeader);

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, $"Host '{hostHeader}' gave {(int)response.StatusCode}");
        await AssertStillIdleAsync(host, hostHeader);
    }

    [Theory]
    [InlineData("GET", "/api/simulation/start")]
    [InlineData("DELETE", "/api/simulation/start")]
    [InlineData("POST", "/api/simulation")]
    [InlineData("GET", "/api/simulation/pause")]
    [InlineData("POST", "/api/simulation/speed")]
    [InlineData("PATCH", "/api/missions/demo-ift/launch-time")]
    [InlineData("POST", "/kml/live")]
    [InlineData("PUT", "/kml/master")]
    public async Task WrongMethods_AreNotServed_AndNeverMutate(string method, string path)
    {
        await using var host = AttackHost.Start();

        using var response = await host.SendRawAsync(new HttpMethod(method), path, "{\"mission\":\"demo-ift\"}"u8.ToArray(), "application/json", null);

        Assert.True((int)response.StatusCode is 404 or 405, $"{method} {path} gave {(int)response.StatusCode}");
        await AssertStillIdleAsync(host, path);
    }

    [Theory]
    [InlineData("/api/missions/demo-ift%0A")]
    [InlineData("/api/missions/%2e%2e")]
    [InlineData("/api/missions/%E2%80%AEdemo")]
    [InlineData("/api/missions/%F0%9F%9A%80")]
    [InlineData("/api/missions/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("/api/missions/demo%2Fift")]
    [InlineData("/api/missions/demo-ift/trajectory%0A")]
    [InlineData("/api/missions/%FF")]
    public async Task OddMissionIds_Return400Or404_Never5xx(string path)
    {
        await using var host = AttackHost.Start();

        using var response = await host.Client.GetAsync(path, AttackHost.Ct);

        Assert.True((int)response.StatusCode is 400 or 404, $"{path} gave {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData("since=9999-12-31T23:59:59.9999999Z")]
    [InlineData("since=0001-01-01T00:00:00Z")]
    [InlineData("limit=2147483648")]
    [InlineData("limit=99999999999999999999")]
    [InlineData("limit=1e3")]
    [InlineData("vehicle=Starship&vehicle=SuperHeavy")]
    [InlineData("vehicle=%00")]
    [InlineData("since=2026-02-30T00:00:00Z")]
    [InlineData("since=2026-12-01T23:59:60Z")]
    [InlineData("since=%F0%9F%9A%80")]
    public async Task TelemetryQuery_Extremes_AreHandled_Never5xx(string query)
    {
        await using var host = AttackHost.Start();

        using var response = await host.Client.GetAsync($"/api/missions/demo-ift/telemetry?{query}", AttackHost.Ct);

        Assert.True((int)response.StatusCode is 200 or 400, $"?{query} gave {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData("/assets/models/../../appsettings.json")]
    [InlineData("/assets/models/%2e%2e/%2e%2e/README.md")]
    [InlineData("/assets/models/..%2f..%2fREADME.md")]
    [InlineData("/assets/models/..%5c..%5cREADME.md")]
    [InlineData("/control/..%2f..%2fappsettings.json")]
    [InlineData("/control/%2e%2e/appsettings.json")]
    [InlineData("/assets/../ai/NOTES.md")]
    public async Task StaticFiles_PathTraversal_NeverServesOtherFiles(string path)
    {
        await using var host = AttackHost.Start();

        using var response = await host.Client.GetAsync(path, AttackHost.Ct);
        var text = await response.Content.ReadAsStringAsync(AttackHost.Ct);

        Assert.False(
            response.StatusCode == HttpStatusCode.OK && (text.Contains("AllowedHosts") || text.Contains("Starship Mission Tracker") || text.Contains("NOTES")),
            $"{path} served a file outside the static folders");
        Assert.True((int)response.StatusCode < 500, $"{path} gave {(int)response.StatusCode}");
    }

    [Fact]
    public async Task ParallelStarts_ExactlyOneWins_TheRestAre409()
    {
        await using var host = AttackHost.Start();

        var calls = Enumerable.Range(0, 24).Select(async _ =>
        {
            using var response = await host.SendAsync(
                HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"demo-ift\",\"speed\":10,\"countdownSeconds\":30}");
            return (int)response.StatusCode;
        }).ToArray();
        var codes = await Task.WhenAll(calls);

        Assert.Single(codes, c => c == 200);
        Assert.Equal(23, codes.Count(c => c == 409));
        Assert.Equal("Countdown", AttackHost.State(await host.StatusAsync()));
    }

    [Fact]
    public async Task RandomOperationStorm_NeverGives5xx_AndEndsConsistent()
    {
        await using var host = AttackHost.Start();
        var random = new Random(20261120);
        var failures = new List<string>();
        for (var round = 0; round < 12; round++)
        {
            var ops = Enumerable.Range(0, 12).Select(_ => random.Next(8)).ToList();
            var speeds = ops.Select(_ => 1 + random.Next(100)).ToList();
            var batch = ops.Select(async (op, i) =>
            {
                using var response = op switch
                {
                    0 => await host.SendAsync(HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"demo-ift\",\"speed\":50,\"countdownSeconds\":0}"),
                    1 => await host.SendAsync(HttpMethod.Post, "/api/simulation/stop", "{}"),
                    2 => await host.SendAsync(HttpMethod.Post, "/api/simulation/pause", "{}"),
                    3 => await host.SendAsync(HttpMethod.Post, "/api/simulation/resume", "{}"),
                    4 => await host.SendAsync(HttpMethod.Put, "/api/simulation/speed", $"{{\"speed\":{speeds[i]}}}"),
                    5 => await host.PutT0Async("{\"launchTimeUtc\":\"2027-01-01T00:00:00Z\"}"),
                    6 => await host.Client.GetAsync("/kml/live?mission=demo-ift", AttackHost.Ct),
                    _ => await host.Client.GetAsync("/kml/history?mission=demo-ift&r=1", AttackHost.Ct),
                };
                if ((int)response.StatusCode >= 500)
                {
                    var text = await response.Content.ReadAsStringAsync(AttackHost.Ct);
                    lock (failures)
                    {
                        failures.Add($"op {op}: {(int)response.StatusCode} {text}");
                    }
                }
            }).ToArray();
            await Task.WhenAll(batch);
            await host.AdvanceAsync(random.Next(1, 40));
        }

        Assert.Empty(failures);
        var status = await host.StatusAsync();
        var mission = await host.MissionAsync();
        var state = AttackHost.State(status);
        var missionStatus = mission.GetProperty("status").GetString();
        if (state is "Idle")
        {
            Assert.NotEqual("InFlight", missionStatus);
        }

        if (state is "Running" or "Countdown")
        {
            Assert.True(missionStatus is "InFlight" or "Scheduled", $"run {state} but mission status {missionStatus}");
        }

        AttackHost.AssertSaneKml(await host.KmlAsync("live"), "live after storm");
        AttackHost.AssertSaneKml(await host.KmlAsync("history"), "history after storm");
    }

    [Theory]
    [InlineData("9999-12-31T23:59:59Z", Skip = "M2-ATTACK-2: T0 near year 9999 is accepted, then trajectory/events/planned KML throw (500)")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("0001-01-01T00:00:01Z")]
    [InlineData("9999-12-31T00:00:00Z")]
    public async Task PutLaunchTime_ExtremeDates_NeverBreakTheReadEndpoints(string t0)
    {
        await using var host = AttackHost.Start();
        using var put = await host.PutT0Async($"{{\"launchTimeUtc\":\"{t0}\"}}");
        var putStatus = (int)put.StatusCode;
        Assert.True(putStatus is 200 or 400, $"PUT T0 {t0} gave {putStatus}");
        if (putStatus != 200)
        {
            return;
        }

        var broken = new List<string>();
        foreach (var path in new[]
                 {
                     "/api/missions/demo-ift", "/api/missions", "/api/missions/demo-ift/trajectory", "/api/missions/demo-ift/events",
                     "/api/missions/demo-ift/telemetry", "/kml/master?mission=demo-ift", "/kml/planned?mission=demo-ift",
                     "/kml/history?mission=demo-ift", "/kml/live?mission=demo-ift",
                 })
        {
            try
            {
                using var response = await host.Client.GetAsync(path, AttackHost.Ct);
                if ((int)response.StatusCode >= 500)
                {
                    broken.Add($"GET {path} -> {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                broken.Add($"GET {path} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(broken.Count == 0, $"T0 {t0} accepted by PUT but: {string.Join("; ", broken)}");
    }

    [Theory]
    [InlineData("2026-12-01T23:30:00+01:00")]
    [InlineData("2026-12-01T23:30:00")]
    [InlineData("2026-02-30T00:00:00Z")]
    [InlineData("2026-12-01T23:59:60Z")]
    [InlineData("1700000000")]
    [InlineData("")]
    [InlineData("  2026-12-01T23:30:00Z")]
    [InlineData("2026-12-01T23:30:00Z\\n")]
    public async Task PutLaunchTime_BadFormats_Return400_AndChangeNothing(string value)
    {
        await using var host = AttackHost.Start();
        var before = (await host.MissionAsync()).GetProperty("launchTimeUtc").GetString();

        using var response = await host.PutT0Async($"{{\"launchTimeUtc\":\"{value}\"}}");

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"'{value}' gave {(int)response.StatusCode}");
        Assert.Equal(before, (await host.MissionAsync()).GetProperty("launchTimeUtc").GetString());
    }

    [Fact]
    public async Task OversizedBody_Returns413_WithAndWithoutContentLength()
    {
        await using var host = AttackHost.Start();
        var big = "{\"mission\":\"demo-ift\",\"pad\":\"" + new string('x', 17 * 1024) + "\"}";

        var (withLength, _) = await host.TextAsync(HttpMethod.Post, "/api/simulation/start", big);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/simulation/start") { Content = new ChunkedContent(Encoding.UTF8.GetBytes(big)) };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        using var chunked = await host.Client.SendAsync(request, AttackHost.Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, withLength);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
        await AssertStillIdleAsync(host, "oversized");
    }

    [Fact]
    public async Task BodyAtTheLimit_16384Bytes_IsParsed_16385IsRejected()
    {
        await using var host = AttackHost.Start();
        static string Pad(int total)
        {
            const string head = "{\"mission\":\"demo-ift\",\"speed\":0,\"p\":\"";
            return head + new string('a', total - head.Length - 2) + "\"}";
        }

        var (atLimit, _) = await host.TextAsync(HttpMethod.Post, "/api/simulation/start", Pad(16 * 1024));
        var (over, _) = await host.TextAsync(HttpMethod.Post, "/api/simulation/start", Pad((16 * 1024) + 1));

        Assert.Equal(HttpStatusCode.BadRequest, atLimit);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over);
    }

    /// <summary>Content of unknown length (sent chunked, no Content-Length).</summary>
    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
