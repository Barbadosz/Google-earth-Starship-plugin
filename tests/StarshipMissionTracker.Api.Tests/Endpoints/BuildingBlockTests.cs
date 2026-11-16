using System.Net;
using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Dtos;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Api.Services;
using StarshipMissionTracker.Api.Tests.Support;

namespace StarshipMissionTracker.Api.Tests.Endpoints;

/// <summary>Options, filters, URL and time helpers, revision counter.</summary>
public class BuildingBlockTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.10.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.168.1.10", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    public void LoopbackOnlyFilter_IsLoopback(string address, bool expected) =>
        Assert.Equal(expected, LoopbackOnlyFilter.IsLoopback(IPAddress.Parse(address)));

    [Fact]
    public void LoopbackOnlyFilter_NullAddress_IsRejected() => Assert.False(LoopbackOnlyFilter.IsLoopback(null));

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("Application/JSON", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("application/json; charset=UTF-8", true)]
    [InlineData("application/json; charset=iso-8859-1", false)]
    [InlineData("text/plain", false)]
    [InlineData("application/problem+json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void JsonContentTypeFilter_IsJson(string? contentType, bool expected) =>
        Assert.Equal(expected, JsonContentTypeFilter.IsJson(contentType));

    [Fact]
    public void RevisionCounter_IsMonotonicAcrossThreads()
    {
        var counter = new RevisionCounter();
        var values = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 1000, _ => values.Add(counter.Next()));

        Assert.Equal(1000, values.Distinct().Count());
        Assert.Equal(1000, counter.Current);
        Assert.Equal(1, values.Min());
    }

    [Fact]
    public void NetworkLinkOptions_DefaultsMatchPlan()
    {
        var options = new NetworkLinkOptions();

        Assert.Null(options.Validate());
        var settings = options.ToSettings();
        Assert.Equal(TimeSpan.FromSeconds(60), settings.PlannedRefresh);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.HistoryRefresh);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.LiveRefresh);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.MinRefreshPeriod);
    }

    [Theory]
    [InlineData(5, 1, null)]
    [InlineData(0.5, 1, "MinRefreshPeriodSeconds")]
    [InlineData(0, 1, "LiveRefreshSeconds")]
    [InlineData(double.NaN, 1, "LiveRefreshSeconds")]
    [InlineData(5, 0, "MinRefreshPeriodSeconds")]
    public void NetworkLinkOptions_Validate(double live, double min, string? expectedFragment)
    {
        var error = new NetworkLinkOptions { LiveRefreshSeconds = live, MinRefreshPeriodSeconds = min }.Validate();

        if (expectedFragment is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Contains(expectedFragment, error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(15, 1, true)]
    [InlineData(10, 1, false)]
    [InlineData(10.01, 1, true)]
    [InlineData(15, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(double.PositiveInfinity, 1, false)]
    public void LiveOptions_StaleThresholdMustExceedSlowestSampleTime(double staleAfter, double sampleInterval, bool valid) =>
        Assert.Equal(valid, new LiveOptions { StaleAfterSeconds = staleAfter }.Validate(sampleInterval) is null);

    [Theory]
    [InlineData(2000, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(100_001, false)]
    public void HistoryOptions_Validate(int maxPoints, bool valid) =>
        Assert.Equal(valid, new HistoryOptions { MaxPointsPerVehicle = maxPoints }.Validate() is null);

    [Theory]
    [InlineData("Live:StaleAfterSeconds", "5")]
    [InlineData("NetworkLinks:LiveRefreshSeconds", "0.5")]
    [InlineData("History:MaxPointsPerVehicle", "1")]
    public async Task InvalidOptions_FailAtStartup(string key, string value)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestApp.StartAsync(new Dictionary<string, string?> { [key] = value });
        });
    }

    [Theory]
    [InlineData("http", "127.0.0.1:8080", "", "http://127.0.0.1:8080")]
    [InlineData("http", "localhost:9000", "/base", "http://localhost:9000/base")]
    public void ServerUrls_FromRequest(string scheme, string host, string pathBase, string expected)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new Microsoft.AspNetCore.Http.HostString(host);
        context.Request.PathBase = pathBase;

        var urls = ServerUrls.FromRequest(context.Request, new HostFilterOptions().AllowedHostNames);

        Assert.Equal(expected, urls.BaseUrl);
        Assert.Equal(expected + "/control", urls.ControlUrl);
        Assert.Equal(expected + "/kml/live?mission=a%20b", urls.Kml(ServerUrls.LivePath, "a b"));
    }

    [Theory]
    [InlineData(0, "2026-12-01T23:30:00Z")]
    [InlineData(1_230_000, "2026-12-01T23:30:00.123Z")]
    [InlineData(1, "2026-12-01T23:30:00.0000001Z")]
    public void ApiTime_DropsTrailingZeroFractionDigits_AndRoundTrips(long extraTicks, string expected)
    {
        var value = TestData.PlannedT0.AddTicks(extraTicks);

        var text = ApiTime.ToText(value);

        Assert.Equal(expected, text);
        Assert.True(Core.Models.LaunchClock.TryParseUtc(text, out var parsed));
        Assert.Equal(value, parsed);
    }

    [Theory]
    [InlineData("demo-ift", true)]
    [InlineData("A.b_c-1", true)]
    [InlineData("", false)]
    [InlineData("bad code", false)]
    [InlineData("x/y", false)]
    [InlineData("café", false)]
    [InlineData("demo\n", false)]
    public void InputRules_MissionCode(string code, bool valid) => Assert.Equal(valid, InputRules.IsValidMissionCode(code));

    [Fact]
    public void StaleTelemetryTracker_ReportsOnlyChanges()
    {
        var tracker = new StaleTelemetryTracker();
        var t1 = TestData.PlannedT0;
        var t2 = t1.AddSeconds(1);
        const Core.Enums.VehicleType Ship = Core.Enums.VehicleType.Starship;

        Assert.True(tracker.MarkStale("demo-ift", Ship, t1)); // becomes stale: warn
        Assert.False(tracker.MarkStale("DEMO-IFT", Ship, t1)); // same sample, next poll: no warning
        Assert.True(tracker.MarkStale("demo-ift", Core.Enums.VehicleType.SuperHeavy, t1)); // other vehicle
        Assert.True(tracker.MarkStale("demo-ift", Ship, t2)); // a newer sample that is stale again
        Assert.True(tracker.Clear("demo-ift", Ship)); // recovered: log once
        Assert.False(tracker.Clear("demo-ift", Ship));
        Assert.True(tracker.MarkStale("demo-ift", Ship, t2)); // stale again after recovery
    }
}
