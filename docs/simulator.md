# Simulator (M2)

There is no real telemetry source yet. The server contains an internal simulator that **replays the planned path** of
a mission as if it were being flown, and stores the result as telemetry samples. Everything it produces has the data
quality `Simulated` and the source name `Internal simulator` (`SimulatorSource.Name`); the mission is marked
`isSimulated`, and the KML says SIMULATED. Nothing here is real data, and nothing is invented: there is no noise, and a
value the planned path does not have stays empty (null) unless it can be derived from the path (see below).

## What a run does

1. **Start** (control page or `POST /api/simulation/start`, see [api.md](api.md)): the server reads the planned mission
   and computes the wall-clock liftoff instant = now + countdown. In one database transaction it sets the mission to
   `Scheduled`, stores the **simulation anchor** (the wall-clock liftoff instant, used as the T0 of the run), clears an
   old actual launch time and deletes the earlier simulated samples of the mission. The planned T0 is never changed by a
   run. Only a mission with `isSimulated = true` can be started; only one run is active at a time.
2. **Countdown**: the state is `Countdown`, T+ is negative. Speed can be changed (it applies from liftoff); pause is not
   allowed.
3. **Running**: at liftoff the mission becomes `InFlight` and the virtual mission clock starts. Every tick the simulator
   emits all samples that are due and stores them in one batch (catch-up after a slow tick or a high speed loses
   nothing).
4. **Completed**: when every vehicle has emitted its final sample the mission becomes `Completed` and the state is
   `Completed` (T+ stays at the final value). Start can be used again to replay.
5. **Stop**: the run ends and the state is `Idle`. A completed result is kept. An interrupted run goes back to
   `Scheduled` without an actual launch time; the simulation anchor stays when samples were written (they keep their
   time axis until the next Start replaces both), and is cleared when none were written.

The status JSON of every call is described in [api.md](api.md).

## Assumptions

- **Path replay sampler.** For every vehicle with a planned path, a sample is produced at each mission offset
  `n * SampleIntervalSeconds` (default 0.5 s, to match the 0.5 s live refresh), each offset exactly once and in order, starting at the first planned
  point and ending with one final sample exactly at the last planned point (so a vehicle can finish earlier than
  another, for example Super Heavy after landing). The position is interpolated along the planned path; heading and
  pitch come from the path or are derived from its geometry; velocity comes from the path or, if missing, the
  finite-difference speed of the surrounding path segment; roll is only given if the path has it.
- **Time axes.** A sample's `timestampUtc` = T0 of the run + mission offset (the T0 is the simulation anchor); its
  `receivedAtUtc` is the wall-clock instant of the tick that stored it. Samples are validated, and a rejected sample is
  logged and not stored.
- **Mission clock.** Before liftoff: T+ is minus the remaining wall-clock seconds. After liftoff:
  `T+ = anchorOffset + (now - anchorWall) * speed`. Changing the speed, pause and resume re-anchor the clock at that
  instant, so T+ never jumps. While paused T+ is frozen.
- **Tick rate.** A background service wakes up every `Simulation:TickMilliseconds` (default 250 ms, wall clock). At
  speed 10 and 1 s sample spacing a tick therefore emits about 2 to 3 samples per vehicle. After
  `Simulation:MaxConsecutiveFailures` (default 3) failing ticks in a row the run is stopped and the reason is shown in
  the status.
- **Speed range.** `0.1` to `100` times real time, default `Simulation:DefaultSpeed` = 10. Values outside the range are
  rejected with 400, never clamped.

## Speed presets and jump size (control page)

The control page slider has ten steps. The hint under it says how much flight time passes per Google Earth refresh of
the live link (0.5 s, the default `NetworkLinks:LiveRefreshSeconds`): `speed * 0.5` seconds of flight per refresh, that is
how far a vehicle jumps between two updates in Google Earth.

| Speed | Flight time per 0.5 s refresh |
|---|---|
| 0.1x | 0.1 s |
| 0.25x | 0.25 s |
| 0.5x | 0.5 s |
| 1x (real time) | 1 s |
| 2x | 2 s |
| 5x | 5 s |
| 10x | 10 s |
| 20x | 20 s |
| 50x | 50 s |
| 100x | 100 s |

The table shows the jump per refresh at the default 0.5 s. The page's refresh value is a fixed 0.5 s (`LIVE_REFRESH_S`) in the script; if you change `NetworkLinks:LiveRefreshSeconds`, the real jump
size changes but the hint does not. The status JSON also gives `speedMin` and `speedMax` (0.1 and 100).

## Pause and the stale rule

The live KML marks a vehicle **STALE** (name suffix and a note in the balloon: no recent sample, the position may be out
of date) only when all of these hold:

- a run of that mission is `Running` **and not paused**;
- the vehicle's latest sample was **received** more than `Live:StaleAfterSeconds` ago (default 15 s, wall clock, by
  `receivedAtUtc`);
- the vehicle has not reached the end of its planned path (a finished vehicle, such as Super Heavy after landing, is
  expected to have an old sample).

So **a paused simulation is not stale** (no new samples are expected), nor is a Countdown, Completed or Idle state. A
mission with a different run active is not stale either. The threshold must be larger than one sample's wall time at the
slowest speed (`SampleIntervalSeconds / 0.1`), otherwise the server refuses to start. The server logs a warning once
when a vehicle becomes stale and an information message when it recovers.

## Configuration

Section `Simulation` (defaults in `appsettings.json`):

| Key | Default | Meaning |
|---|---|---|
| `DefaultSpeed` | 10 | Speed when a start request has none; also used by auto-start. |
| `SampleIntervalSeconds` | 0.5 | Spacing of the samples on the mission axis (finite, > 0). |
| `TickMilliseconds` | 250 | Wake-up period (>= 1). |
| `MaxConsecutiveFailures` | 3 | Failing ticks in a row before the run is stopped (>= 1). |
| `AutoStart` | false | Start a run automatically after the database is ready. |
| `AutoStartMissionCode` | empty | Mission to start (required when `AutoStart` is on). |
| `AutoStartCountdownSeconds` | 10 | Countdown of the auto-started run, 0 to 86400. |

`appsettings.Development.json` sets `AutoStart` to true for `demo-ift`. With `dotnet run` that writes simulated samples
into the real `data/starship.db` on every start (the next run deletes the previous simulated samples of that mission).
Invalid values stop the server at start with a message.
