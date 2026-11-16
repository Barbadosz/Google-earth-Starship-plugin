using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Api.Startup;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Api.Tests.Wiring;

/// <summary>Bad configuration stops the host at startup with a readable reason (N-30, reviews N-26 and N-29).</summary>
public class StartupValidationTests
{
    public static TheoryData<string, string, string> BadSettings => new()
    {
        { "Simulation:DefaultSpeed", "0", "DefaultSpeed" },
        { "Simulation:DefaultSpeed", "100.5", "DefaultSpeed" },
        { "Simulation:SampleIntervalSeconds", "0", "SampleIntervalSeconds" },
        { "Simulation:TickMilliseconds", "0", "TickMilliseconds" },
        { "Simulation:MaxConsecutiveFailures", "0", "MaxConsecutiveFailures" },
        { "Simulation:AutoStart", "true", "AutoStartMissionCode" },
        { "Live:StaleAfterSeconds", "5", "StaleAfterSeconds" },
        { "NetworkLinks:LiveRefreshSeconds", "0", "NetworkLinks" },
        { "History:MaxPointsPerVehicle", "1", "History" },
        { "HostFilter:AllowedHostNames:0", "localhost:8080", "HostFilter" },
        { "urls", "http://0.0.0.0:8080", "not loopback" },
        { "urls", "http://*:8080", "not loopback" },
        { "Paths:Assets", "no-such-assets-folder", "Assets folder not found" },
    };

    [Theory]
    [MemberData(nameof(BadSettings))]
    public async Task BadConfiguration_FailsAtStartup(string key, string value, string expectedFragment)
    {
        await using var factory = new StarshipAppFactory(new Dictionary<string, string?> { [key] = value });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(expectedFragment, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SimulationRules_AcceptDefaults_AndAutoStartWithMission()
    {
        Assert.Null(SimulationOptionsRules.Validate(new SimulationOptions()));
        Assert.Null(SimulationOptionsRules.Validate(new SimulationOptions { AutoStart = true, AutoStartMissionCode = "demo-ift", AutoStartCountdownSeconds = 0 }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(86_401)]
    public void SimulationRules_RejectBadAutoStartCountdown(double countdown)
    {
        var options = new SimulationOptions { AutoStart = true, AutoStartMissionCode = "demo-ift", AutoStartCountdownSeconds = countdown };

        Assert.Contains("AutoStartCountdownSeconds", SimulationOptionsRules.Validate(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("http://[::1]:8080", true)]
    [InlineData("http://127.0.0.2:8080", true)]
    [InlineData("http://0.0.0.0:8080", false)]
    [InlineData("http://[::]:8080", false)]
    [InlineData("http://*:8080", false)]
    [InlineData("http://+:8080", false)]
    [InlineData("http://192.168.1.10:8080", false)]
    [InlineData("http://example.com:8080", false)]
    public void LoopbackBinding_IsLoopback(string url, bool expected) => Assert.Equal(expected, LoopbackBinding.IsLoopback(url));

    [Theory]
    [InlineData("localhost:8080")]
    [InlineData("::1")]
    [InlineData("[::1]:8080")]
    [InlineData("[::1")]
    [InlineData("::1]")]
    public void HostFilterOptions_RejectPortsAndUnbracketedIpv6(string entry)
    {
        var options = new HostFilterOptions { AllowedHostNames = ["localhost", entry] };

        Assert.NotNull(options.Validate());
    }

    [Theory]
    [InlineData("[::1]")]
    [InlineData("my-pc")]
    [InlineData("127.0.0.1")]
    public void HostFilterOptions_AcceptPlainNamesAndBracketedIpv6(string entry)
    {
        var options = new HostFilterOptions { AllowedHostNames = ["localhost", entry] };

        Assert.Null(options.Validate());
    }

    [Fact]
    public void StarshipPaths_ResolveRelativeToTheRepositoryRoot()
    {
        var paths = StarshipPaths.Resolve(new PathOptions(), AppContext.BaseDirectory);

        Assert.True(Directory.Exists(Path.Combine(paths.RootDirectory, "data", "missions")));
        Assert.Equal(Path.Combine(paths.RootDirectory, "data", "starship.db"), paths.DatabaseFile);
        Assert.Equal(Path.Combine(paths.RootDirectory, "assets"), paths.AssetsDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "logs"), paths.LogDirectory);
    }

    [Fact]
    public void StarshipPaths_NoRepositoryRoot_Throws()
    {
        var empty = Path.Combine(Path.GetTempPath(), "starship-noroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => StarshipPaths.Resolve(new PathOptions(), empty));
            Assert.Contains("Paths:Root", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }
}
