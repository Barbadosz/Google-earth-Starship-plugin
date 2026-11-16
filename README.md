# Starship Mission Tracker

A .NET tool that builds a **Google Earth Pro KMZ** for a Starship flight: planned 3D flight paths (Starship and
Super Heavy), 3D models, navigation icons and mission events on the Google Earth time slider. The launch time (T0) is
a setting, so a slipped launch only needs one changed value.

The project has two parts:

- **M1 - KMZ command line tool.** Builds a static KMZ file you open in Google Earth Pro.
- **M2 - local server.** An ASP.NET Core server with a SQLite database that serves the same mission as live-updating
  KML (Google Earth Network Links). There is no real telemetry source yet: the position is **simulated** by replaying
  the planned path at a chosen speed. The server only listens on this computer (loopback).

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

## Local server (M2)

### Start

```
dotnet run --project src/StarshipMissionTracker.Api
```

Run it from the repository root. The server listens on `http://127.0.0.1:8080` and **only on loopback**: it refuses
to start with a listen address that is not loopback, and the endpoints that change something are not available from
other computers (see [HTTP API](docs/api.md)). On start it creates or migrates the SQLite database
(`data/starship.db`) and imports the missions from `data/missions`. Logs go to the console and to `logs/`.

> **Warning (development).** `appsettings.Development.json` turns `Simulation:AutoStart` on. With `dotnet run`
> (Development environment) every start therefore begins a simulation of `demo-ift` and writes **simulated samples
> into the real `data/starship.db`**. Set `Simulation:AutoStart` to `false` (or run in another environment) if you
> do not want that. Simulated samples are always labelled `Simulated`.

### Control page

Open <http://127.0.0.1:8080/control> in a browser. It shows the simulation state, T+ and the mission time, and has the
Start, Stop and Pause/Resume buttons and the speed slider.

- **Start** begins a countdown (default 10 s, a field on the page), then the vehicles move along the planned path.
- **Pause** freezes the mission time, **Resume** continues. Pause is not possible during the countdown.
- **Speed** changes immediately (during the countdown it applies from liftoff). The slider has the steps
  0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 50 and 100 times real time, see [Simulator](docs/simulator.md).
- **Stop** ends the run (an interrupted run goes back to `Scheduled`).

Only one run is active at a time; a second Start while one is active is refused (409).

### Google Earth Pro: Network Link

1. Start the server and make sure the control page opens.
2. In Google Earth Pro choose **Add > Network Link**.
3. Name: anything, for example `Starship (local)`. Link: `http://127.0.0.1:8080/kml/master`. Do not open `output/demo-ift.kmz` for this: that static file never refreshes. With `dotnet run` (Development) a `demo-ift` run auto-starts; to start by hand set `$env:Simulation__AutoStart="false"` first (PowerShell). A Start during an active run answers 409.
4. Press OK. Google Earth loads the master document and flies to the launch site with the time slider at T0.
5. The master holds three links that refresh themselves: planned trajectory and events (60 s), actual trajectory so
   far (1 s) and the vehicles at their latest sample (1 s).
6. Start a simulation on the control page and watch the vehicles move. The "Flight path curtain" and "Event labels"
   folders can be switched off with their checkboxes.

Google Earth takes the refresh intervals from the master, only when the link is loaded, so after changing them in `appsettings.json`
refresh the master link (right-click > Refresh; not verified in GE: if the new intervals do not appear, remove and re-add the Network Link). Details: [KML server](docs/kml-server.md).

### Changing the launch time (T0)

The planned T0 of a mission is changed with `PUT /api/missions/{code}/launch-time`, for example:

```
curl -X PUT -H "Content-Type: application/json" -d '{"launchTimeUtc":"2026-12-01T23:30:00Z"}' http://127.0.0.1:8080/api/missions/demo-ift/launch-time
```

It is only allowed while no run of that mission is active (state Idle or Completed). During a countdown or a run
(also while paused) the answer is `409`: stop the simulation first. A successful change also resets the simulation
anchor and deletes the simulated samples of that mission, so the next run starts clean. Google Earth shows the new T0
the next time the master or planned link is loaded or refreshed.

### Server documentation

- [HTTP API](docs/api.md)
- [KML server (master, Network Links, toggles)](docs/kml-server.md)
- [Simulator](docs/simulator.md)
- [Data model](docs/DATAMODEL.md)

## Command line options (M1)

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
data/starship.db                     SQLite database of the server (not committed)
src/                                 Core, Trajectory, Kml, Cli, Data, Telemetry, Api
tests/                               xUnit tests
docs/                                documentation
```

The KMZ contains `doc.kml` and the models and icons under `files/`.

## Documentation

- [HTTP API](docs/api.md)
- [KML server](docs/kml-server.md)
- [Simulator](docs/simulator.md)
- [Data model](docs/DATAMODEL.md)
- [Google Earth Pro check (manual)](docs/ge-ellenorzes.md)
- [Data sources and data quality](docs/data-sources.md)
- [Calculation assumptions](docs/calculations.md)
- [Altitude reference](docs/altitude-reference.md)
- [Assets and third-party licences](docs/assets-licenses.md)

## Licence

MIT, see [LICENSE](LICENSE).
