# Google Earth Pro check for M1 (manual)

This is the manual part of the M1 Definition of Done. The automated tests check the XML, the structure and the
numbers. Only you can check what Google Earth Pro (GE Pro) really shows.

Tick each box, and write anything odd in the **Findings** section at the end.

## 0. Build and open

1. Build the KMZ from the repository root. Pick a launch time in the near future (UTC, ending in `Z`):

   ```
   dotnet run --project src/StarshipMissionTracker.Cli -- --mission demo-ift --launch-time 2026-12-01T14:00:00Z
   ```

2. The file is `output/demo-ift.kmz`. Open it in GE Pro (File > Open).
3. Keep the time slider visible (View > Show Time Slider if it is hidden).

The demo data is **illustrative** (quality `Simulated`). It is not real flight data. Positions, timings and shapes only
need to look sensible; they are not exact.

- [ ] The file opens without an error message.
- [ ] The Places panel shows one top item for the mission.

## 1. Placement at the launch site

- **Look for:** fly to the start of the path. The Starship model stands on the launch site in south Texas (Starbase,
  about 25.997 N, 97.157 W), at the coast.
- **Wrong:** the model is far away, in the ocean, in another country, or under or high above the ground.
  Latitude and longitude swapped would put it near 97 N / 26 W (not possible, so GE would show an odd place).
- [ ] The model is at the launch site, on the ground (height about 5 m).

## 2. Model orientation

The model's nose points along +Z in the model file. Orientation comes from the `gx:angles` of the track: heading is the
direction of travel, tilt is `90 - pitch`, roll is 0 (see [altitude-reference.md](altitude-reference.md) and
[assets-licenses.md](assets-licenses.md), section "KML Orientation mapping").

- **Look for:** at T+0 the nose points straight up. During ascent the nose leans towards the east (the demo flight goes
  east over the Gulf of Mexico) and slowly tips towards horizontal. The nose always points forward along the path.
- **Wrong:** the vehicle lies down at T+0, flies backwards (tail first), leans to the wrong side (west), or turns
  around its own axis.
- [ ] At T+0 the model stands upright.
- [ ] During ascent the nose points along the direction of travel.
- [ ] Model size looks right (Starship about 50 m, Super Heavy about 72 m tall; they are placeholder shapes).
- [ ] If the sign of tilt or roll is wrong, write which one and how in Findings. The convention in
  `assets-licenses.md` is then corrected.

## 3. Planned paths (blue, 3D, extruded)

- **Look for:** a blue line for each vehicle, high above the ground, with a thin vertical "curtain" down to the ground.
  Since N-39a the line itself is not extruded; the curtain is the separate folder "Flight path curtain" (section 11). The line follows the curve of the Earth. The Starship path goes far east, to the Indian
  Ocean. The Super Heavy path is short and comes back to the launch site.
- **Wrong:** the line cuts through the ground, is green or orange (blue is planned), has no curtain, or the curtain is
  missing on the long Starship path.
- [ ] Starship planned path is blue and follows the curve of the Earth (no cutting below the surface).
- [ ] Super Heavy planned path is blue.
- [ ] The line has a vertical curtain to the ground.

## 4. Layer folders

The Places panel follows spec section 9.1: Mission Information, Planned Trajectory (Starship, Super Heavy),
Actual Trajectory (empty in M1), Vehicles (for each vehicle a 3D model and an icon folder), Mission Events. Since N-39a
there are two more: "Flight path curtain" (after Planned Trajectory) and "Event labels" (after Mission Events), see
section 11.

- **Look for:** untick and tick each folder. The matching thing appears and disappears alone.
- **Wrong:** a folder is missing, is in the wrong place, or unticking it hides something else.
- [ ] The folder hierarchy matches the list above.
- [ ] Each folder can be switched on and off alone.
- [ ] The Mission Information text shows the launch time (UTC) and "planned launch time, subject to change".
- [ ] The text says planned/reconstructed, not live telemetry.
- [ ] The Mission Information balloon ends with a "Flight timeline" table (T+ time, event name, description).

## 5. Time slider animation from T0

- **Look for:** set the slider to the start. Drag it or press play. The vehicle moves along its path. Events appear
  at their time. The slider range starts at T0 (the launch time from step 0) and ends about 65 minutes later
  (T+3900 s).
- **Wrong:** the vehicle does not move, it jumps, it is always visible, the slider range is in 1970 or in the wrong
  year, or the vehicle moves along a different line than the blue path.
- [ ] The slider starts at the launch time you used (UTC).
- [ ] The vehicle moves smoothly along the blue path.
- [ ] The Super Heavy comes back to the launch site (see also section 7).

## 6. Changing T0

1. Change the launch time with either way:
   - run the CLI with another `--launch-time` (it wins over the file), or
   - edit `launchTimeUtc` in `data/missions/demo-ift.json`.
2. Build again (same command as in step 0).
3. Reload in GE Pro. GE may keep the old file in its cache. Use File > Revert (or remove the item from Places and open
   the KMZ again). If the old time is still shown, close GE Pro and open it again.

- [ ] After reload, the slider range and the event times show the new launch time.
- [ ] The path shape did not change (only the times).
- [ ] Note in Findings if you needed more than File > Revert.

## 7. Super Heavy and boostback

- **Look for:** the Super Heavy booster goes up with the Starship, separates (about T+160 s), turns back (boostback
  event about T+172 s), and returns to the launch site (landing event about T+520 s). Its icon differs from the
  Starship icon (orange capsule instead of white arrowhead).
- **Wrong:** the booster follows the Starship all the way, lands far away, or its model is not shown.
- [ ] The Super Heavy path and model are there and move.
- [ ] The boostback and landing events are near the right place.

## 8. Model and icon switch (Lod)

The 3D model is only drawn when you are close (Region with `minLodPixels` = 2000, a placeholder value from
`KmlLod.ModelMinLodPixels`). The navigation icon has no Region and is always visible.

- **Look for:** zoom out until the model disappears and only the icon is left; zoom in until the model appears.
- **Wrong:** the vehicle disappears completely at some zoom level, or the model flickers on and off, or you see the
  model from very far away (threshold too low), or never see it (threshold too high).
- [ ] At some zoom the icon is shown alone, and the model at a closer zoom.
- [ ] The vehicle is never gone (icon or model is always visible).
- [ ] Write in Findings at what height (eye altitude) the switch happens, and if you want it closer or farther.

## 9. Event placemarks

- **Look for:** 12 events (for example Liftoff, Max Q, MECO, Stage Separation, Boostback, Reentry, Splashdown).
  Each has a time stamp, so it appears at its time on the slider. Click one to see the description. It says that it is
  illustrative and `Simulated`.
- **Wrong:** an event is at the wrong place, always visible, or has no description. The Coast event has no position
  and may not show as a point (this is expected).
- [ ] Events appear at the time of the slider and have a readable balloon text.
- [ ] The event icon is a small circle. It is loaded from the internet (Google's built-in icon). Offline, the icon
  is missing. That is a known limit, not a bug.

## 10. Performance

`doc.kml` is about 0.93 MB (0.86 MB before N-39a; the curtain folder repeats the planned path coordinates once
more). The path data appears about 5 times per vehicle (the static line, its curtain, the time trail, the moving
vehicle track, ...). The Starship path has about 1970 points after densification.

- **Look for:** is rotating, zooming and moving the time slider smooth?
- **Wrong:** freezing, slow redraw, long loading time.
- [ ] Opening the file takes a few seconds at most.
- [ ] Moving the slider and the view is smooth.
- [ ] Write in Findings: computer type (rough), GE Pro version, and how smooth it is. This is the input for N-20
  (optimising the document size).

## 11. Toggle layers and Sun at T0 (N-39a, N-40a)

Two folders can be switched on and off on their own: "Flight path curtain" (the vertical walls under the planned path)
and "Event labels" (the "T+hh:mm:ss Name" texts next to the event icons). The event icons themselves have no label any
more. The document view carries the launch time (`gx:TimeStamp` = T0), so opening the file should put the time
slider and the Sun at T0. KML cannot switch the Sun on: turn it on by hand (View > Sun). The slider shows local time
unless Tools > Options > "Show time in UTC" (or similar) is set; that is not a bug.

- **Look for (Cli KMZ):** with View > Sun on, open `output/demo-ift.kmz`. The light and shadows match the launch time
  (14:00 UTC is morning in Texas). Double-click the Mission Information placemark: the view flies to the launch site
  and the slider jumps back to T0.
- **Look for (server):** open the master KML (`/kml/master`). The Sun and slider go to T0 (the simulation launch time
  if a simulation has run, otherwise the planned T0). After the NetworkLinks refresh the camera and the slider do not
  move.
- **Wrong:** the Sun follows the computer clock after opening, the curtain or the labels cannot be switched off, the
  folder shows its children in the Places panel, a label has a visible icon, or a checkbox switches back on by itself.
- [ ] Cli KMZ: opening the file sets the Sun / slider to T0.
- [ ] Server master: opening it sets the Sun / slider to T0; a NetworkLink refresh does not move camera or slider.
- [ ] "Flight path curtain" and "Event labels" each show one checkbox only (no child items listed).
- [ ] Unticking "Flight path curtain" leaves the blue line without walls; unticking "Event labels" leaves the icons
  without text.
- [ ] Label placemarks show only text (no icon, no pin), at the same place as the event icon.
- [ ] Server mode: untick both folders, wait for at least two refreshes of "Planned trajectory & events" (or change
  the launch time): both stay unticked.
- [ ] Performance (section 10) is still fine with the bigger `doc.kml` (about 0.93 MB, KMZ about 183 KB).

## Findings

Write here what you found. For each point: the step number, what you saw, what you expected. A screenshot is
welcome if it contains no personal data.

```
Step:
Seen:
Expected:
```
