# Google Earth Pro check for M2 (manual, live server)

This is the manual part of the M2 Definition of Done (the local server, see the README section "Local server (M2)").
The automated tests check the XML, the HTTP answers and the simulator. Only you can check what Google Earth Pro
(GE Pro) really shows while the simulation runs. The M1 file check is in [ge-ellenorzes.md](ge-ellenorzes.md).

Tick each box, and write anything odd in the **Findings** section at the end.

All data here is **simulated** (quality `Simulated`, source `Internal simulator`). It is a replay of the planned path,
not telemetry. Background: [api.md](api.md), [kml-server.md](kml-server.md), [simulator.md](simulator.md).

Defaults used below (from `appsettings.json`): live link refresh 1 s, history link 1 s, planned link 60 s, stale
threshold 15 s. If you changed `NetworkLinks` values, use yours. Google Earth takes the intervals from the master, so after a change refresh the master link (right-click > Refresh; not verified in GE: if the new intervals do not appear, remove and re-add the Network Link).

## 0. Prepare

1. Close other programs that use port 8080.
2. **Remove the static KMZ.** If GE Pro has `output/demo-ift.kmz` (the M1 command-line file) open, remove that layer
   in Places (right-click > Delete) or untick it. That file is a frozen copy with its own T0 (2026-12-01 14:00); it
   never moves and never refreshes. The server check only works with the Network Link of step 1.3.
3. **Turn off the development auto-start (mandatory).** `appsettings.Development.json` (used by `dotnet run`) has
   `Simulation:AutoStart=true`, mission `demo-ift`, countdown 10 s, so a run starts by itself and **writes simulated
   samples into the real `data/starship.db`**. Set it to `false` before starting, in PowerShell:

   ```
   $env:Simulation__AutoStart="false"
   ```

   (bash: `export Simulation__AutoStart=false`). Run `dotnet run` in the same console. If the server is already
   running with auto-start, press **Stop** on the control page and then **Start** at `1x`. A Start while a run is
   active is refused: `POST /api/simulation/start` answers `409`.
4. Keep the time slider visible in GE Pro (View > Show Time Slider).

- [ ] I removed the old `demo-ift.kmz` layer from GE Pro.
- [ ] I turned auto-start off (or I will Stop and Start by hand).
- [ ] I know that a simulation run replaces the earlier simulated samples of `demo-ift` in `data/starship.db`.

## 1. Start the server and add the Network Link

1. From the repository root (same console where you set the variable):

   ```
   dotnet run --project src/StarshipMissionTracker.Api
   ```

2. Open <http://127.0.0.1:8080/control> in a browser (it redirects to `/control/index.html`).
3. In GE Pro: **Add > Network Link** (do **not** use File > Open and do not open a `.kmz`). Name `Starship (local)`,
   Link `http://127.0.0.1:8080/kml/master`. Press OK.

- **Look for:** the console shows the listen address `http://127.0.0.1:8080`. The control page shows state `Idle`
  (or `Countdown`/`Running` if auto-start was left on). GE flies to the launch site (Starbase, south Texas). Under the
  `Starship (local)` link in Places there are three child folders: "Planned trajectory & events", "Actual trajectory
  (SIMULATED)" and "Vehicles (SIMULATED, live)". The time slider date is **not** 2026-12-01 14:00 (that is the T0
  of the static CLI KMZ); it is the server T0 (see section 8).
- **Wrong:** a network link error text, no child folders under the link, the slider at 2026-12-01 14:00 (you have the
  static KMZ open), or the view stays somewhere else.
- [ ] The server starts and the control page opens.
- [ ] The master loads without an error and the three child folders are there.
- [ ] The planned blue paths, the event icons and the Mission Information placemark are visible.

## 2. Live movement

1. On the control page set the speed to `1x`, countdown 10, press **Start**.
2. Zoom in on the launch site (close, or you see only the icons). Watch for at least 2 minutes after liftoff.

- **Look for:** after the countdown the Starship and the Super Heavy move along the
  blue path. The **orange actual track** (simulated data is drawn in the orange estimated style, never green; green is only for published or measured data) grows behind each vehicle. The vehicle position updates about every second
  (live link) and the orange track about every second (history link). The camera and the time slider do **not** move on
  a refresh (`flyToView=0`); you can navigate freely and the view stays where you put it. The 3D model is drawn only
  close up (Region `minLodPixels` 2000, so zoom in until the vehicle is large); from far away only the icon is visible.
- **Wrong:** the camera jumps back to the launch site, the slider changes, the vehicle does not move, the orange line
  does not grow, or the vehicle is shown twice.
- [ ] The vehicles move and the orange actual track grows.
- [ ] The camera does not jump on any refresh (watch at least 3 live refreshes).
- [ ] The live position and the orange track update about every second (or your configured values).
- [ ] The history refresh interval is the configured `NetworkLinks:HistoryRefreshSeconds` (1 s by default).
- [ ] The vehicle balloon (click the model or icon) shows the data and the SIMULATED notice.

## 3. Speeds (N-34)

Do this on the same run (Stop and Start again if it has ended). The slider steps are 0.1, 0.25, 0.5, 1, 2, 5, 10, 20,
50, 100. The hint under the slider shows how much flight time passes per 1 s refresh (for example 10x = 10 s).

1. Start at `0.5x`. Watch two or three live refreshes (about 0.5 s of flight per refresh: small steps).
2. While it runs, switch to `10x`, then to `100x`.
3. Press **Pause**. Wait more than 20 s (longer than the stale threshold of 15 s).
4. Press **Resume**.
5. Switch to `100x`, then back to `0.1x`.
6. Click the vehicle balloon link(s) (any link in the balloon, for example the control page address).

- **Look for:** the control page T+ never jumps on a speed change or on pause/resume. In GE the vehicles move in bigger
  steps at a higher speed (100x = about 100 s of flight per refresh, a large but expected jump), but there is no
  jump back, no duplicate model, no flicker caused by the change itself. While paused the vehicle stands still and its
  name does **not** get " - STALE" (a paused run is not stale). After Resume the vehicles move on from where they
  stopped.
- **Wrong:** a jump backwards or to a strange place at a speed change, " - STALE" during the pause, no movement after
  Resume, a balloon link that cannot be clicked or opens a wrong page.
- [ ] 0.5x moves in small steps.
- [ ] Switching 0.5x -> 10x -> 100x while running causes no backward jump.
- [ ] Pause: the vehicles stand still and are **not** marked STALE (check after more than 20 s).
- [ ] Resume: movement continues from the paused position.
- [ ] 100x -> 0.1x works without a jump.
- [ ] The link in the vehicle balloon is clickable.

## 4. Changing T0 (planned launch time)

There is no T0 field on the control page. The change is a request to the API (README "Changing the launch time").

1. While a simulation is **running** (or in the countdown, or paused) send:

   ```
   curl -i -X PUT -H "Content-Type: application/json" -d '{"launchTimeUtc":"2026-12-01T23:30:00Z"}' http://127.0.0.1:8080/api/missions/demo-ift/launch-time
   ```

2. Press **Stop** on the control page. Send the same request again.
3. Reload the master link in GE Pro (right-click the link > Refresh; unverified in GE, if the new values do not appear remove and re-add the link; GE reads the
   intervals and the initial view only on load).

- **Look for:** step 1 answers `409` with a JSON `error: conflict` text, and the planned T0 does not change; the
  control page keeps working. Step 2 answers `200` with the mission header (`plannedLaunchTimeUtc` =
  `2026-12-01T23:30:00Z`). After the reload the planned path times, the event times (Mission Information timeline) and
  the time slider range follow the new T0, the path shape stays the same. The simulated samples of the mission are gone
  (the orange track is empty).
- **Wrong:** the change is accepted during a run (200), the shape of the path changes, the slider/timeline still show
  the old T0 after a reload.
- [ ] While running (also paused): `409`, nothing changed.
- [ ] After Stop: `200`, new planned T0 in the answer.
- [ ] After a reload the planned path, the events and the slider follow the new T0.
- [ ] Note in Findings whether the 409 is visible on the control page at all (the page has no T0 control; the 409 is in
  the curl answer).

## 5. Stop and restart

1. Start a run, let it fly for about a minute, then press **Stop**.
2. Look at GE after the next refreshes (a few seconds).
3. Press **Start** again.

- **Look for:** the control page goes to `Idle`. A stopped run keeps the samples that exist until the next Start; the
  vehicles stay at the last position. After the new Start the old orange track and the old vehicle positions are
  **cleared** (the earlier simulated samples are deleted), and the vehicles start again from the launch site; the
  orange track grows from the beginning.
- **Wrong:** the new run continues the old track, old orange lines stay next to the new one, duplicate vehicles, or
  a second Start during an active run is accepted (it must give an error on the page: only one run is active).
- [ ] After a new Start the old track is gone and the redraw starts at the launch site.
- [ ] A Start while a run is active shows an error on the control page and changes nothing.

## 6. Remote 3D model over HTTP (N-15)

The `.dae` models are not inside a KMZ in server mode. GE loads them from the server
(`http://127.0.0.1:8080/assets/models/...`). The cookie `r=<n>` makes every refresh URL different, and the server
sends no-cache headers.

1. Zoom close to a vehicle during a 1x run. The 3D model appears only close up (Region `minLodPixels` 2000); from far away you see only the icon.
2. Watch at least 5 live refreshes (5 s) without moving the camera.

- **Look for:** the 3D model of the vehicle is shown once. It moves, but does not flash, disappear for a moment or
  appear twice (old and new copy) when the live link refreshes. The model orientation is the same as in M1 (nose forward
  along the path).
- **Wrong:** two models at once, a model that blinks off at each refresh, a model stuck at an old position, a missing
  model with only the icon shown close up.
- [ ] The model loads from the server and is shown.
- [ ] No duplicate or flashing model during 5 refreshes.
- [ ] Optional: in a browser open `http://127.0.0.1:8080/kml/live` twice; the cookie `r=` in the document grows.

## 7. Toggle folders and the timeline (N-39a)

1. In Places untick **Flight path curtain** and **Event labels**.
2. Let the planned link refresh at least 2 times (about 2 minutes at 60 s) while a simulation runs.
3. Open the Mission Information balloon (click the placemark at the launch site).

- **Look for:** both folders stay **unticked** across the refreshes (`refreshVisibility=0`), the wall under the blue
  line and the "T+hh:mm:ss Name" texts stay hidden, the line and the event icons stay. The balloon ends with a
  "Flight timeline" table (T+ time, name, description) and shows the SIMULATED notice and the control page address as text.
- **Wrong:** a checkbox switches back on by itself, the folders list their children, the table is missing.
- [ ] "Flight path curtain" stays unticked after 2 or more refreshes.
- [ ] "Event labels" stays unticked after 2 or more refreshes.
- [ ] Ticking them again shows the curtain and the labels again.
- [ ] The Mission Information balloon has the "Flight timeline" T+ table.

## 8. Sun and time slider at T0 (N-40a, N-41)

N-41 (commit 50c75db) carries the launch site into the mission information, so the master `LookAt` is over the launch
site.

1. Switch the Sun on (View > Sun).
2. Remove the Network Link, and add `http://127.0.0.1:8080/kml/master` again.
3. Do it twice: once with no simulation run since the server start (or after a T0 change, which resets the anchor),
   once after a run.

- **Look for:** after loading, the camera flies to the launch site and the slider and the Sun go to T0. T0 is the
  simulation anchor (the liftoff instant of the last run) if a run set one, otherwise the planned T0. Later refreshes do
  not move the camera or the slider. The slider shows your local time unless GE is set to UTC.
- **Wrong:** the Sun follows the computer clock, the camera flies to another place (not Starbase), the slider is at
  1970 or at the wrong date, a refresh moves the slider.
- [ ] The camera flies to the launch site (south Texas) on load.
- [ ] The slider and the Sun are at T0 on load (planned T0 without a run, simulation anchor after a run).
- [ ] A later refresh does not move the camera or the slider.

## 9. Stopping the server

1. Press **Ctrl+C** in the console where `dotnet run` runs.
2. Watch GE for the next link refreshes.
3. Start the server again (section 1, command) and look at the control page and at GE.

- **Look for:** the server ends cleanly (a run that was active is interrupted, state goes back to `Scheduled` in the
  database; no exception). GE shows a network link error for the child links while the server is down, which is
  expected, and recovers after the next start without re-adding the link (a child refresh works; reload the master if
  an interval changed). Logs are in `logs/`.
- **Remember:** with the development auto-start on, every `dotnet run` starts a new `demo-ift` simulation and
  **rewrites the simulated samples in `data/starship.db`**. Set `Simulation:AutoStart` to `false` if you do not want that.
- **Wrong:** the process hangs after Ctrl+C, an unhandled exception in the console or log, or GE does not recover
  after the server is back.
- [ ] Ctrl+C stops the server cleanly.
- [ ] GE shows an error for the links while the server is down, and recovers after the next start.
- [ ] I noticed that auto-start rewrites the simulated samples in `data/starship.db`.

## Findings

Write here what you found. For each point: the step number, what you saw, what you expected. Include the GE Pro
version and the speed used. A screenshot is welcome if it contains no personal data.

```
Step:
Seen:
Expected:
```
