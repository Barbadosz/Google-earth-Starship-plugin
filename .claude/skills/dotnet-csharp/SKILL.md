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
- Packages: `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`. v3 test projects are executables (`OutputType Exe`), the `dotnet new xunit3` template sets this up.
- .NET 10 `dotnet test` supports Microsoft.Testing.Platform natively; if the runner complains about VSTest mode, check `global.json` `"test": { "runner": "Microsoft.Testing.Platform" }` (verify against the installed SDK, do not guess).
- Plain `Assert`, no FluentAssertions. Names `Method_Condition_Result`. Theory data for reference values, with source in a comment.
- Run through the test runner of `ai-protokoll` (`teszt.mjs`), command is `dotnet test StarshipMissionTracker.sln`.
- Golden files: store under `tests/<proj>/Golden/`, copy to output via csproj `<None Update ... CopyToOutputDirectory>`. Regenerating a golden file is a deliberate, reviewed act.

## Logging
- `Microsoft.Extensions.Logging` + `Serilog.Extensions.Hosting`/`Serilog.Sinks.File`+`Console`. Library code takes `ILogger<T>` only.
- File: `logs/starship-.log` (`rollingInterval: Day`), relative to the working directory; run from the repo root so it lands in `D:\Dev\Starship google earth\logs\`. Level from `appsettings.json` (Debug in dev, Information in release).
- Grep the log for the mission code / exception type; never read it whole. Never log secrets.

## Configuration
- `appsettings.json` + `IOptions<T>` with `ValidateOnStart`. Secrets: `dotnet user-secrets` or env vars.
- Paths (assets, data, output) come from options, resolved relative to the repo root, never absolute.

## Common mistakes
- `TreatWarningsAsErrors` + nullable: fix the warning, do not add `!` or `#pragma` to silence it.
- Forgetting that `assets/*.dae` must be copied/packed into the KMZ `files/` folder (see `kml-kmz` skill), not into bin.
- Python in `tools/` is data prep only, never called at runtime.
