using StarshipMissionTracker.Api.Startup;

// Starship Mission Tracker server (M2): wiring only, see Startup/StarshipServer.cs.
// Listens on loopback only (appsettings "Urls", default http://127.0.0.1:8080; --urls to change the port).
// The content root (wwwroot, appsettings*.json) is located from the application's folder, not the working directory.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ContentRootLocator.Find(AppContext.BaseDirectory),
});
try
{
    builder.Services.AddStarshipServer(builder.Configuration);
    var app = builder.Build();
    app.UseStarshipServer();
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // The host logger may not exist yet (bad paths, bad options): report on the console, then fail with the exception.
    await Console.Error.WriteLineAsync($"Server startup failed: {ex.GetType().Name}: {ex.Message}");
    throw;
}

// Makes the entry point visible to WebApplicationFactory<Program> in the tests.
public partial class Program { }
