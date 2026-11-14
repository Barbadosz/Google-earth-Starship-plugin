namespace StarshipMissionTracker.Cli.Options;

/// <summary>Outcome of <see cref="CliArgumentParser.Parse"/>: options, a help request, or a usage error.</summary>
public sealed record CliParseResult
{
    private CliParseResult(CliOptions? options, bool showHelp, string? error)
    {
        Options = options;
        ShowHelp = showHelp;
        Error = error;
    }

    public CliOptions? Options { get; }
    public bool ShowHelp { get; }
    public string? Error { get; }

    public static CliParseResult Success(CliOptions options) => new(options, false, null);

    public static CliParseResult Help() => new(null, true, null);

    public static CliParseResult Failure(string error) => new(null, false, error);
}
