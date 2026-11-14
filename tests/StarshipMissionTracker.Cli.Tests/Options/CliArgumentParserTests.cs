using StarshipMissionTracker.Cli.Options;

namespace StarshipMissionTracker.Cli.Tests.Options;

public class CliArgumentParserTests
{
    [Fact]
    public void Parse_MissionOnly_UsesDefaults()
    {
        var result = CliArgumentParser.Parse(["--mission", "demo-ift"]);

        var options = Assert.IsType<CliOptions>(result.Options);
        Assert.Equal("demo-ift", options.MissionCode);
        Assert.Null(options.LaunchTimeOverrideUtc);
        Assert.Equal("data", options.DataDirectory);
        Assert.Equal("assets", options.AssetsDirectory);
        Assert.Equal("output", options.OutputDirectory);
        Assert.Equal("demo-ift.kmz", options.OutputFileName);
    }

    [Fact]
    public void Parse_AllOptions_SpaceAndEqualsForms()
    {
        var result = CliArgumentParser.Parse(
            ["--mission=demo-ift", "--launch-time", "2026-12-01T14:00:00Z", "--data", "d", "--assets=a", "--output", "out/"]);

        var options = Assert.IsType<CliOptions>(result.Options);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero), options.LaunchTimeOverrideUtc);
        Assert.Equal(TimeSpan.Zero, options.LaunchTimeOverrideUtc!.Value.Offset);
        Assert.Equal("d", options.DataDirectory);
        Assert.Equal("a", options.AssetsDirectory);
        Assert.Equal("out/", options.OutputDirectory);
    }

    [Fact]
    public void Parse_MissingMission_Fails()
    {
        var result = CliArgumentParser.Parse(["--launch-time", "2026-12-01T14:00:00Z"]);

        Assert.Null(result.Options);
        Assert.Contains("--mission", result.Error);
    }

    [Theory]
    [InlineData("2026-12-01")]
    [InlineData("2026-12-01T14:00:00")]
    [InlineData("2026-12-01T14:00:00+01:00")]
    [InlineData("2026-12-01 14:00:00Z")]
    [InlineData("tomorrow")]
    public void Parse_BadLaunchTime_FailsWithClearMessage(string value)
    {
        var result = CliArgumentParser.Parse(["--mission", "demo-ift", "--launch-time", value]);

        Assert.Null(result.Options);
        Assert.Contains("Invalid launch time", result.Error);
        Assert.Contains("2026-12-01T14:00:00Z", result.Error);
    }

    [Fact]
    public void Parse_FractionalLaunchTime_Accepted()
    {
        var result = CliArgumentParser.Parse(["--mission", "demo-ift", "--launch-time", "2026-12-01T14:00:00.5Z"]);

        Assert.Equal(500, result.Options!.LaunchTimeOverrideUtc!.Value.Millisecond);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_Help_ShowsHelp(string flag)
    {
        var result = CliArgumentParser.Parse(["--mission", "x", flag]);

        Assert.True(result.ShowHelp);
        Assert.Contains("--launch-time", CliArgumentParser.Usage);
    }

    [Theory]
    [InlineData("--mission", "../secret")]
    [InlineData("--mission", "a/b")]
    [InlineData("--mission", "-x")]
    public void Parse_UnsafeMissionCode_Fails(string option, string value)
    {
        var result = CliArgumentParser.Parse([$"{option}={value}"]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("Aux")]
    [InlineData("prn")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    [InlineData("demo\n")]
    public void Parse_ReservedDeviceNameOrTrailingNewline_Fails(string code)
    {
        var result = CliArgumentParser.Parse(["--mission", code]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("console")]
    [InlineData("com10")]
    [InlineData("nul-1")]
    public void Parse_NamesOnlyStartingLikeDeviceNames_Pass(string code)
    {
        Assert.NotNull(CliArgumentParser.Parse(["--mission", code]).Options);
    }

    [Fact]
    public void Parse_UnknownOption_Fails()
    {
        var result = CliArgumentParser.Parse(["--mission", "demo-ift", "--verbose"]);
        Assert.Contains("Unknown option '--verbose'", result.Error);
    }

    [Fact]
    public void Parse_DuplicateOption_Fails()
    {
        var result = CliArgumentParser.Parse(["--mission", "a", "--mission", "b"]);
        Assert.Contains("more than once", result.Error);
    }

    [Fact]
    public void Parse_OptionWithoutValue_Fails()
    {
        var result = CliArgumentParser.Parse(["--mission", "demo-ift", "--launch-time"]);
        Assert.Contains("needs a value", result.Error);
    }

    [Fact]
    public void Parse_StrayArgument_Fails()
    {
        var result = CliArgumentParser.Parse(["demo-ift"]);
        Assert.Contains("Unexpected argument", result.Error);
    }
}
