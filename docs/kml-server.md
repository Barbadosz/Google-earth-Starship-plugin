# KML server mode (M2)

In server mode Google Earth Pro loads one master document and the master pulls three child documents over time
(NetworkLinks). The endpoints are in [api.md](api.md); the static KMZ of M1 is described in the README. Everything the
server writes about a simulated mission is marked **SIMULATED**: positions come from an internal simulator replaying
planned data, not from telemetry ([simulator.md](simulator.md)).

## Documents

```
/kml/master?mission=<code>
  NetworkLink planned-link   -> /kml/planned?mission=<code>   refresh 60 s
  NetworkLink history-link   -> /kml/history?mission=<code>   refresh 0.5 s
  NetworkLink live-link      -> /kml/live?mission=<code>      refresh 0.5 s
```

The master only holds the three links, a description (with the SIMULATED notice for a simulated mission) and the
initial view. The link URLs are absolute and built from the request's `Host` (a non-local `Host` is replaced by
`127.0.0.1:<port>`). The master is built per request, so a changed T0 shows on the next open or reload of the link.

Each link is `refreshMode=onInterval` with `refreshInterval` from the configuration, `visibility=1`, `open=0`,
`refreshVisibility=0` and `flyToView=0`. `refreshVisibility=0` keeps the checkbox state the user set; `flyToView=0`
means a refresh never moves the camera or the time slider.

| Document | Link name | Content |
|---|---|---|
| planned | "Planned trajectory & events" | Folders "Mission Information", "Planned Trajectory", "Flight path curtain", "Mission Events", "Event labels". |
| history | "Actual trajectory (SIMULATED)" | Folder "Actual Trajectory": the stored actual (simulated) path per vehicle, decimated to at most `History:MaxPointsPerVehicle` (10000) points per vehicle, first and last kept. Empty when there is no actual path. |
| live | "Vehicles (SIMULATED, live)" | Folder "Vehicles": per vehicle a 3D model and an icon at the latest sample, with a balloon. Not time-stamped, so the time slider never hides them. A vehicle marked **STALE** has " - STALE" in its name (rule in [simulator.md](simulator.md)). |

For a non-simulated mission the link names carry no "(SIMULATED)".

## Refresh intervals

Configuration section `NetworkLinks` in `appsettings.json` (seconds, each in (0, 86400]):

| Key | Default | Meaning |
|---|---|---|
| `PlannedRefreshSeconds` | 60 | Interval of the planned link. |
| `HistoryRefreshSeconds` | 0.5 | Interval of the history link (sub-second allowed, written as an invariant decimal, e.g. `0.5`). |
| `LiveRefreshSeconds` | 0.5 | Interval of the live link (sub-second allowed). |
| `MinRefreshPeriodSeconds` | 0.25 | `minRefreshPeriod` in every child document's NetworkLinkControl; no interval may be smaller. |

Google Earth Pro takes the intervals from the master, and only when the master link is loaded. After changing them, refresh the master link (right-click > Refresh; not verified in GE: if the new intervals do not appear, remove and re-add the Network Link); the running child links keep their old interval.

Cost of a refresh (N-48, N-53): the live document is about 3-7 KB. The history document grows with the samples, capped by `History:MaxPointsPerVehicle` (10000 per vehicle; a 1 h flight at 0.5 s is about 7200 rows, and above the cap the decimation picks shift between refreshes): measured about 29 bytes per point (a 1340-point diagnosis sample was 44 KB, 13.6 KB gzipped), so about 29 bytes per kept point. The display lines (path, curtain) are thinned (8 m tolerance), so only the full-resolution model and icon tracks grow with the cap. Estimate, not measured: two vehicles at the 10000-point cap (the old 2000-point cap gave about 130 KB raw / 40 KB gzipped) would be up to about 600 KB raw and 200 KB gzipped; a 1 h run at 0.5 s (about 7200 points per vehicle) about 400 KB raw and 130 KB gzipped. The history document is built per request (no caching, `no-store`), reading the samples from the local SQLite database; this is cheap on one machine. If it ever becomes heavy, raise `HistoryRefreshSeconds`.

`Live:StaleAfterSeconds` (default 15) and `History:MaxPointsPerVehicle` are separate sections. Invalid values stop the
server at start with a message.

## Cookie and no-cache behaviour (N-15)

- Every KML response has `Cache-Control: no-store, no-cache, must-revalidate, max-age=0`, `Pragma: no-cache`,
  `Expires: 0` and no `ETag` / `Last-Modified`. Responses are gzip compressed when the client accepts it.
- Each child document starts with a `NetworkLinkControl` holding `minRefreshPeriod` and `cookie` = `r=<revision>`.
  The revision is a process-wide counter that grows with every child response. Google Earth appends the cookie to the
  next request of that link (`...?mission=demo-ift&r=42`), so the URL differs on every refresh and caches cannot serve an
  old copy. The server ignores the `r` parameter. The master has no cookie.
- Errors are plain text so Google Earth shows them as the link error (`400` bad mission code, `404` unknown mission).
- The planned document's expensive part (loading and densifying the paths) is cached per mission, keyed by the mission's
  update time and the effective T0, so any T0 change rebuilds it; the KML text itself is built per request.

## Folders and toggle layers (N-39a)

Folders with a fixed `id` attribute, so tests and tools can find them:

| Folder name | `id` | Where | Content |
|---|---|---|---|
| Flight path curtain | `curtain` | planned (and KMZ) | One extruded polygon placemark per planned path with the same coordinates as the path; no visible line, fill colour of the path style. |
| Event labels | `event-labels` | planned (and KMZ) | One label placemark per event that has coordinates, named `T+hh:mm:ss <name>`, at the same position as the event icon. |
| Mission Events | none | planned | The event icons (icon only, no label) with the planned UTC time in the balloon. |

The two layers are separate folders so they can be switched off with their checkbox: the label text and the
curtain can be hidden without touching the path or the icons. The planned path itself is a plain line (`extrude=0`);
the extruded "curtain" look comes from the curtain folder. Actual paths keep their own extrusion. All three use the
folder style `toggle-folder` (`ListStyle checkHideChildren`), which shows the folder as one checkbox in the places
panel without a child list. Event icon placemarks use a style with label scale 0, the label placemarks use
`event-label` (icon scale 0, label scale 0.8), and the curtain placemarks use `curtain-<path style id>` styles.

"Mission Information" holds the mission placemark at the launch site. Its balloon shows the planned T0, status,
data-quality note, the SIMULATED notice for a simulated mission, the control page address (as text), and a "Flight timeline"
table of the events (T+hh:mm:ss, name, description; inline CSS only, no script).

## Initial view and the time slider (N-40a)

The master document has a `LookAt` over the launch site (range, tilt and heading are fixed defaults) with a
`gx:TimeStamp` whose `when` is T0 of the mission. When Google Earth flies to this view it puts the time slider and the
Sun at T0. T0 here is the **effective T0**:

```
effective T0 = simulation anchor (if a run set one)  ??  planned T0
```

This is the `launchTimeUtc` field of the mission header ([api.md](api.md)), the same value every T+ label and every
event time uses. Only the master carries the time-stamped `LookAt`; the child documents use `flyToView=0`, so a refresh
does not move the slider. After a T0 change (or a new run, which sets a new anchor) the new T0 appears the next time the
master is loaded. The static KMZ (M1) carries the same `LookAt` with the T0 of its mission clock.

## Notes

- The KML uses the models and icons from `/assets/models` and `/assets/icons` of the same server (placeholder models).
- Altitudes are MSL, see [altitude-reference.md](altitude-reference.md). A manual Google Earth check list is in
  [ge-ellenorzes.md](ge-ellenorzes.md).
