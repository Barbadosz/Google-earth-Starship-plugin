using StarshipMissionTracker.Cli.Options;
using StarshipMissionTracker.Cli.Pipeline;
using StarshipMissionTracker.Core.Exceptions;
using Serilog;
using Serilog.Events;

namespace StarshipMissionTracker.Cli;

/// <summary>
/// Console entry point. Exit codes: 0 = KMZ written (or help shown), 1 = data/file error, 2 = invalid command line,
/// 130 = cancelled with Ctrl+C. Command-line errors (exit 2) go to the console only, not to the log file.
/// </summary>
internal static class Program
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitUsage = 2;
    public const int ExitCancelled = 130;

    private static async Task<int> Main(string[] args)
    {
        var parsed = CliArgumentParser.Parse(args);
        if (parsed.ShowHelp)
        {
            Console.WriteLine(CliArgumentParser.Usage);
            return ExitOk;
        }

        if (parsed.Options is not { } options)
        {
            await Console.Error.WriteLineAsync($"Error: {parsed.Error}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("Run with --help for usage.").ConfigureAwait(false);
            return ExitUsage;
        }

        Log.Logger = CreateLogger();
        try
        {
            Log.Information(
                "Starting: mission {MissionCode}, launch-time override {LaunchTimeOverride}, data {DataDirectory}, assets {AssetsDirectory}, output {OutputDirectory}",
                options.MissionCode,
                options.LaunchTimeOverrideUtc?.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", System.Globalization.CultureInfo.InvariantCulture) ?? "(none)",
                options.DataDirectory, options.AssetsDirectory, options.OutputDirectory);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var pipeline = new MissionKmzPipeline(Log.Logger);
            var (result, outputPath) = await pipeline.RunAsync(options, cts.Token).ConfigureAwait(false);
            Log.Information(
                "Done: {OutputFile}, T0 {LaunchTime}, {VehicleCount} vehicles, {PointCount} points, {EventCount} events, entries {Entries}",
                MissionKmzPipeline.Display(outputPath), result.LaunchTimeUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                result.VehicleCount, result.PointCount, result.EventCount, result.EntryNames);
            return ExitOk;
        }
        catch (MissionDataException ex)
        {
            Log.Error(ex, "Mission data error ({MissionCode}): {Message}", options.MissionCode, ex.Message);
            return ExitError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "File error ({MissionCode}): {Message}", options.MissionCode, ex.Message);
            return ExitError;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Cancelled ({MissionCode})", options.MissionCode);
            return ExitCancelled;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unexpected error ({MissionCode}): {Message}", options.MissionCode, ex.Message);
            return ExitError;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Console (short, no stack traces) + daily rolling file logs/starship-YYYYMMDD.log (with exceptions).</summary>
    private static Serilog.Core.Logger CreateLogger()
    {
        var logFile = Path.Combine(Directory.GetCurrentDirectory(), "logs", "starship-.log");
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}")
            .WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
