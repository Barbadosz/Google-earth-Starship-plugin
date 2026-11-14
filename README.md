# Starship Mission Tracker

A .NET tool that builds a **Google Earth Pro KMZ** for a Starship flight: planned 3D flight paths (Starship and
Super Heavy), 3D models, navigation icons and mission events on the Google Earth time slider. The launch time (T0) is
a setting, so a slipped launch only needs one changed value.

This is milestone M1 (a static KMZ built by a command line tool). A backend and a database come later.

## Disclaimer

This is an unofficial hobby project. It is **not affiliated with, endorsed by or connected to SpaceX**.
The included demo mission (`demo-ift`) is **illustrative only**: its trajectory, events and times are rough, made-up
values (quality `Simulated`), not official data and not telemetry. Do not use it for navigation, safety or any
real-world decision. The 3D models and icons are simple generated placeholders. No SpaceX images or models are used.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json`)
- Google Earth Pro (desktop) to view the result

## Build, test, run

```
dotnet build StarshipMissionTracker.sln
dotnet test StarshipMissionTracker.sln
dotnet run --project src/StarshipMissionTracker.Cli -- --mission demo-ift --launch-time 2026-12-01T14:00:00Z
```

Run the commands from the repository root. The result is `output/demo-ift.kmz`; open it in Google Earth Pro.

## Command line options

```
StarshipMissionTracker.Cli --mission <code> [--launch-time <T0>] [--data <dir>] [--assets <dir>] [--output <dir>]
```

| Option | Meaning | Default |
|---|---|---|
| `--mission <code>` | Mission code (required). Reads `<data>/missions/<code>.json`. | - |
| `--launch-time <T0>` | Launch time override, UTC ISO 8601 ending in `Z`, for example `2026-12-01T14:00:00Z`. Wins over `launchTimeUtc` in the mission file. | value in the mission file |
| `--data <dir>` | Input data folder. | `data` |
| `--assets <dir>` | Models and icons folder. | `assets` |
| `--output <dir>` | Output folder. The file is `<code>.kmz`. | `output` |
| `-h`, `--help` | Show help. | |

If neither `--launch-time` nor `launchTimeUtc` is set, the tool stops with an error (no default launch time).

Exit codes: `0` ok, `1` data or file error, `2` invalid command line, `130` cancelled (Ctrl+C).

## Layout

```
data/missions/<code>.json            mission, events, launch time (T0)
data/trajectories/<code>-<vehicle>-planned.json   planned trajectory points (T+ seconds)
assets/models, assets/icons          placeholder 3D models (.dae) and icons (.png)
output/<code>.kmz                    generated KMZ (not committed)
logs/                                log files (not committed)
src/                                 Core, Trajectory, Kml, Cli
tests/                               xUnit tests
docs/                                documentation
```

The KMZ contains `doc.kml` and the models and icons under `files/`.

## Documentation

- [Google Earth Pro check (manual)](docs/ge-ellenorzes.md)
- [Data sources and data quality](docs/data-sources.md)
- [Calculation assumptions](docs/calculations.md)
- [Altitude reference](docs/altitude-reference.md)
- [Assets and third-party licences](docs/assets-licenses.md)

## Licence

MIT, see [LICENSE](LICENSE).
