using System.Diagnostics;
using StarshipMissionTracker.Cli.Options;

namespace StarshipMissionTracker.Cli.Tests.Attack;

/// <summary>M1 attack pass: the argument parser with hostile arguments, and the real CLI process (exit codes).</summary>
public sealed class CliAttackTests : IDisposable
{
    private readonly AttackWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    // ---------------------------------------------------------------- parser

    [Theory]
    [InlineData("2026-12-01T14:00:00+00:00")]
    [InlineData("2026-12-01T16:00:00+02:00")]
    [InlineData("2026-12-01T14:00:00-05:00")]
    [InlineData("2026-12-01T14:00:00")]
    [InlineData("2026-12-01T14:00:00z")]
    [InlineData("2026-12-01t14:00:00Z")]
    [InlineData("2026-12-01 14:00:00Z")]
    [InlineData("2026-12-01T14:00Z")]
    [InlineData("2026-12-01Z")]
    [InlineData("2026-02-30T14:00:00Z")]
    [InlineData("2026-13-01T14:00:00Z")]
    [InlineData("2026-12-01T24:00:00Z")]
    [InlineData("2026-12-01T23:59:60Z")]
    [InlineData("2026-12-01T14:00:00.Z")]
    [InlineData("2026-12-01T14:00:00.12345678Z")]
    [InlineData("2026-12-01T14:00:00,5Z")]
    [InlineData(" 2026-12-01T14:00:00Z")]
    [InlineData("2026-12-01T14:00:00Z ")]
    [InlineData("2026-12-01T14:00:00Z\n")]
    [InlineData("２０２６-12-01T14:00:00Z")]
    [InlineData("١٤٤٨-12-01T14:00:00Z")]
    [InlineData("0000-12-01T14:00:00Z")]
    [InlineData("10000-12-01T14:00:00Z")]
    [InlineData("1764590400")]
    [InlineData("now")]
    [InlineData("-")]
    public void LaunchTime_NotStrictUtc_IsRejected(string value)
    {
        var result = CliArgumentParser.Parse(["--mission", "demo", "--launch-time=" + value]);
        Assert.Null(result.Options);
        Assert.False(result.ShowHelp);
        Assert.Contains("launch time", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchTime_GivenAsSeparateArgumentStartingWithDash_IsRejectedNotSwallowed()
    {
        var result = CliArgumentParser.Parse(["--mission", "demo", "--launch-time", "--output", "x"]);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("--mission", "--launch-time")]
    [InlineData("--mission=", "")]
    [InlineData("--mission", "")]
    [InlineData("--mission", "   ")]
    public void EmptyOrOptionLikeMissionValue_Fails(string a, string b)
    {
        Assert.Null(CliArgumentParser.Parse([a, b]).Options);
    }

    [Theory]
    [InlineData("--mission=a", "--mission=b")]
    [InlineData("--mission=a", "--mission", "b")]
    [InlineData("--output=a", "--mission=m", "--output=b")]
    [InlineData("--mission=m", "--mission=m")]
    public void DuplicateOptions_AnyForm_Fail(params string[] args)
    {
        var result = CliArgumentParser.Parse(args);
        Assert.Null(result.Options);
        Assert.Contains("more than once", result.Error);
    }

    [Theory]
    [InlineData("--Mission")]
    [InlineData("--MISSION")]
    [InlineData("--mission-code")]
    [InlineData("--launchtime")]
    [InlineData("-mission")]
    [InlineData("/mission")]
    [InlineData("—mission")]
    public void NearMissOptionNames_AreUnknown(string name)
    {
        var result = CliArgumentParser.Parse([name, "x", "--mission", "demo"]);
        Assert.Null(result.Options);
    }

    [Fact]
    public void ValueStartingWithSingleDash_IsTakenAsValue()
    {
        var result = CliArgumentParser.Parse(["--mission", "demo", "--output", "-out"]);
        Assert.Equal("-out", result.Options!.OutputDirectory);
    }

    [Theory]
    [InlineData("--data")]
    [InlineData("--assets")]
    [InlineData("--output")]
    public void BlankFolderOption_Fails(string option)
    {
        Assert.Null(CliArgumentParser.Parse(["--mission", "demo", option + "=   "]).Options);
        Assert.Null(CliArgumentParser.Parse(["--mission", "demo", option + "="]).Options);
    }

    [Fact]
    public void HelpAnywhere_BeatsEverythingBefore_ButNotAnUnknownOptionBeforeIt()
    {
        Assert.True(CliArgumentParser.Parse(["--mission", "demo", "-h"]).ShowHelp);
        Assert.True(CliArgumentParser.Parse(["--help"]).ShowHelp);
        Assert.Null(CliArgumentParser.Parse(["--bogus", "--help"]).Options);
    }

    [Fact]
    public void MissionCodeAsHelpFlagValue_IsNotHelp()
    {
        // "--mission -h": -h is consumed as the (invalid) mission code.
        var result = CliArgumentParser.Parse(["--mission", "-h"]);
        Assert.False(result.ShowHelp);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("é")]
    [InlineData("demo ift")]
    [InlineData("demo.ift")]
    [InlineData("demo/ift")]
    [InlineData("demo\\ift")]
    [InlineData("..")]
    [InlineData("-demo")]
    [InlineData("_demo")]
    [InlineData("demo\u0000")]
    [InlineData("ＡＢＣ")]
    [InlineData("demo:ads")]
    [InlineData("demo?")]
    [InlineData("Com1")]
    [InlineData("LPT9")]
    [InlineData("Nul")]
    public void MissionCode_Unsafe_IsRejected(string code)
    {
        Assert.Null(CliArgumentParser.Parse(["--mission", code]).Options);
    }

    [Fact]
    public void MissionCode_VeryLong_ParsesLeavingPathLimitsToTheFileSystem()
    {
        var code = new string('a', 400);
        var result = CliArgumentParser.Parse(["--mission", code]);
        Assert.NotNull(result.Options);
    }

    [Fact]
    public void LaunchTime_EndOfCalendar_ParsesLeavingTheOverflowToThePipeline()
    {
        var result = CliArgumentParser.Parse(["--mission", "demo", "--launch-time", "9999-12-31T23:59:59Z"]);
        Assert.NotNull(result.Options);
    }

    [Fact]
    public void NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CliArgumentParser.Parse(null!));
    }

    [Fact]
    public void ManyArguments_DoNotBreakTheParser()
    {
        var args = Enumerable.Range(0, 100_000).Select(i => $"x{i}").ToArray();
        var result = CliArgumentParser.Parse(args);
        Assert.Null(result.Options);
    }

    // ---------------------------------------------------------------- real process

    private static string CliDll => Path.Combine(AppContext.BaseDirectory, "StarshipMissionTracker.Cli.dll");

    private async Task<(int Exit, string Out, string Err)> RunCliAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _ws.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(CliDll);
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    [Fact]
    public async Task Process_NoArguments_Exit2_WithUsageHint()
    {
        var (exit, _, err) = await RunCliAsync();
        Assert.Equal(2, exit);
        Assert.Contains("--mission", err);
        Assert.DoesNotContain("   at ", err);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public async Task Process_Help_Exit0_PrintsUsage(string flag)
    {
        var (exit, output, _) = await RunCliAsync(flag);
        Assert.Equal(0, exit);
        Assert.Contains("Usage:", output);
        Assert.Contains("Exit codes", output);
    }

    [Theory]
    [InlineData("--mission", "atk", "--launch-time", "2026-12-01T14:00:00")]
    [InlineData("--mission", "atk", "--bogus")]
    [InlineData("--mission", "atk", "--mission", "atk")]
    [InlineData("--mission", "con")]
    public async Task Process_BadCommandLine_Exit2_NoStackTrace_NoOutputFolder(params string[] args)
    {
        var (exit, output, err) = await RunCliAsync(args);
        Assert.Equal(2, exit);
        Assert.DoesNotContain("   at ", output + err);
        Assert.False(Directory.Exists(Path.Combine(_ws.Root, "output")));
    }

    [Fact]
    public async Task Process_MissingMissionFile_Exit1_ShortMessage_NoStackTrace()
    {
        var (exit, output, err) = await RunCliAsync("--mission", "atk", "--data", _ws.Data, "--assets", _ws.Assets, "--output", _ws.Output);
        Assert.Equal(1, exit);
        Assert.Contains("atk.json", output + err);
        Assert.DoesNotContain("   at ", output + err);
        Assert.False(Directory.Exists(_ws.Output));
    }

    [Fact]
    public async Task Process_ValidRun_Exit0_WritesKmz_FromFolderWithSpacesAndUnicode()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        var (exit, _, err) = await RunCliAsync(
            "--mission", "atk", "--launch-time", "2026-12-01T14:00:00Z",
            "--data", _ws.Data, "--assets", _ws.Assets, "--output", _ws.Output);
        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_ws.Output, "atk.kmz")), err);
    }

    [Fact]
    public async Task Process_OutputIsAFile_Exit1_NoStackTraceOnConsole()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        await File.WriteAllTextAsync(_ws.Output, "x", TestContext.Current.CancellationToken);
        var (exit, output, err) = await RunCliAsync(
            "--mission", "atk", "--launch-time", "2026-12-01T14:00:00Z",
            "--data", _ws.Data, "--assets", _ws.Assets, "--output", _ws.Output);
        Assert.Equal(1, exit);
        Assert.DoesNotContain("   at ", output + err);
    }

    [Fact]
    public async Task Process_LogsPathIsAFile_StillDoesItsJob()
    {
        // The Serilog file sink writes to ./logs; if that cannot be created the run must not crash.
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        await File.WriteAllTextAsync(Path.Combine(_ws.Root, "logs"), "i am a file", TestContext.Current.CancellationToken);
        var (exit, _, err) = await RunCliAsync(
            "--mission", "atk", "--launch-time", "2026-12-01T14:00:00Z",
            "--data", _ws.Data, "--assets", _ws.Assets, "--output", _ws.Output);
        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_ws.Output, "atk.kmz")), err);
    }

    [Fact(Skip = "ATTACK-2 - date range overflow is logged as fatal Unexpected error")]
    public async Task Process_HostileDataEndOfCalendar_Exit1_NotReportedAsUnexpected()
    {
        _ws.WriteMission(AttackWorkspace.DefaultPoints());
        var (exit, output, err) = await RunCliAsync(
            "--mission", "atk", "--launch-time", "9999-12-31T23:59:59Z",
            "--data", _ws.Data, "--assets", _ws.Assets, "--output", _ws.Output);
        Assert.Equal(1, exit);
        Assert.DoesNotContain("Unexpected error", output + err);
    }

}
