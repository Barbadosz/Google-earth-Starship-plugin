using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace StarshipMissionTracker.Api.Tests.Wiring;

/// <summary>
/// The real Program.cs host on a TestServer with a temp SQLite file and a temp log folder (environment "Testing", so the
/// Development auto-start is off unless a test sets it). Data and assets come from the repository (read only).
/// </summary>
public sealed class StarshipAppFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    public StarshipAppFactory(IDictionary<string, string?>? settings = null)
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "starship-wiring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
        DatabaseFile = Path.Combine(TempDirectory, "db", "starship.db");
        _settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Paths:Database"] = DatabaseFile,
            ["Paths:Logs"] = Path.Combine(TempDirectory, "logs"),
            ["Simulation:AutoStart"] = "false",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    public string TempDirectory { get; }

    public string DatabaseFile { get; }

    /// <summary>A client with Host 127.0.0.1:8080 that does not follow redirects.</summary>
    public HttpClient CreateLocalClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("http://127.0.0.1:8080"),
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held by the OS is left in the temp folder; not a test failure.
        }
    }
}
