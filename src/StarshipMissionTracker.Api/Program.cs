// Placeholder host (M2 package 0). Real wiring comes in package H.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Starship Mission Tracker API (placeholder)");

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the tests.
public partial class Program { }
