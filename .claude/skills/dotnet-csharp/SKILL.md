---
name: dotnet-csharp
description: Read before creating or editing any C# project, solution, props file, test project, logging or config in Starship Mission Tracker (.NET 10, xUnit v3, Serilog).
---

# .NET 10 / C# in this project

Rules of the project are in `ai/KONVENCIOK.md`; this file only lists what is easy to get wrong.

## Solution and build
- .NET 10 SDK: `dotnet new sln` creates `.slnx` by default. The project uses `StarshipMissionTracker.sln`, so run `dotnet new sln --format sln`.
- Central package management: versions only in `Directory.Packages.props` (`<PackageVersion>`), `.csproj` has `<PackageReference Include="X" />` without Version. A Version in a csproj gives NU1008.
- `Directory.Build.props`: `TargetFramework net10.0`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`. These files are hot spots: only the wiring package touches them.
- `Core` has no NuGet references (not even logging abstractions beyond BCL). `Kml` references only `Core` + trajectory models.
- Add projects with `dotnet sln add`, references with `dotnet add reference`; never hand-edit the .sln GUIDs.

## Language patterns
- File-scoped namespaces, one public type per file, namespace = folder.
- Use `TimeProvider` instead of `DateTime.Now/UtcNow`. All timestamps UTC.
- Culture: any number-to-text in KML/CSV must use `CultureInfo.InvariantCulture` (Hungarian locale prints commas!). Test it under `hu-HU` culture.
- `record` / `init` for immutable models (points, events). Units in names (`AltitudeMeters`).
- Invalid input: `ArgumentException`/own exception, never a silent 0.

## Testing (xUnit v3)
- Package: only `xunit.v3` (version in `Directory.Packages.props`). With Microsoft.Testing.Platform, `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` are NOT needed; do not add them. v3 test projects are executables (`OutputType Exe`). The `xunit3` template is not installed on SDK 10.0.404; copy an existing test csproj instead.
- `global.json` must keep `"test": { "runner": "Microsoft.Testing.Platform" }`; without it `dotnet test` fails in VSTest mode on the .NET 10 SDK (verified 2026-11-14, SDK 10.0.404).
- Plain `Assert`, no FluentAssertions. Names `Method_Condition_Result`. Theory data for reference values, with source in a comment.
- Run through the test runner of `ai-protokoll` (`teszt.mjs`), command is `dotnet test StarshipMissionTracker.sln`.
- Golden files: store under `tests/<proj>/Golden/`, copy to output via csproj `<None Update ... CopyToOutputDirectory>`. Regenerating a golden file is a deliberate, reviewed act.

## Time (TimeProvider)
- Inject `TimeProvider` (register `TimeProvider.System` in `Program.cs`); `GetUtcNow()` replaces `DateTime.UtcNow`. KML generators do not read a clock: they get `nowUtc`/T0 as parameters.
- Tests: own `ManualTimeProvider : TimeProvider` (override `GetUtcNow`, `Advance(TimeSpan)`), no extra NuGet. `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) exists but is deliberately not used here. For simulator timers/delays use `timeProvider.CreateTimer`/`Task.Delay(..., timeProvider)`, otherwise the fake clock does not drive them; prefer a testable `TickAsync()` method over waiting for the loop.

## Api (Minimal API, `Microsoft.NET.Sdk.Web`)
- Kestrel bound to `http://127.0.0.1:8080` (loopback only) via config. Endpoints in `Api/Endpoints/` as `MapXxx(this IEndpointRouteBuilder)` extension methods; `Program.cs` only wires (hot spot).
- `Api`, `Data`, `Telemetry` log through `ILogger<T>` (never `Serilog.Log` static); Serilog is the host provider (`builder.Host.UseSerilog(...)`, package `Serilog.AspNetCore`). The Cli keeps using Serilog directly.
- Simulator = `BackgroundService` (`ExecuteAsync` with `stoppingToken`); catch and log exceptions in the loop, a throw stops the host by default. Do not use scoped services directly (scope factory).
- Options: `IOptions<T>` + `ValidateOnStart`. Mutating endpoints check `HttpContext.Connection.RemoteIpAddress` is loopback.
- Tests: integration with `WebApplicationFactory<Program>` (`Microsoft.AspNetCore.Mvc.Testing`; needs `public partial class Program { }`); override services in `ConfigureWebHost` (`TimeProvider` -> ManualTimeProvider, temp SQLite file path, disable the real simulator `BackgroundService`). Fallback if Mvc.Testing misbehaves under xUnit v3 / Microsoft.Testing.Platform (UNVERIFIED in this repo; the package is built around the VSTest-era setup): use `Microsoft.AspNetCore.TestHost` (`new HostBuilder().ConfigureWebHost(w => w.UseTestServer()...)` or `WebApplication` + `UseTestServer`) and `GetTestClient()`. Delete the temp db in `Dispose`.

## Logging
- `Microsoft.Extensions.Logging` + `Serilog.Extensions.Hosting`/`Serilog.Sinks.File`+`Console`. Library code takes `ILogger<T>` only.
- File: `logs/starship-.log` (`rollingInterval: Day`), relative to the working directory; run from the repo root so it lands in `logs/` under the repo root. Level from `appsettings.json` (Debug in dev, Information in release).
- Grep the log for the mission code / exception type; never read it whole. Never log secrets.

## Configuration
- `appsettings.json` + `IOptions<T>` with `ValidateOnStart`. Secrets: `dotnet user-secrets` or env vars.
- Paths (assets, data, output) come from options, resolved relative to the repo root, never absolute.

## Common mistakes
- `TreatWarningsAsErrors` + nullable: fix the warning, do not add `!` or `#pragma` to silence it.
- Forgetting that `assets/*.dae` must be copied/packed into the KMZ `files/` folder (see `kml-kmz` skill), not into bin.
- Python in `tools/` is data prep only, never called at runtime.
