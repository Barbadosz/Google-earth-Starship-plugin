# HTTP API (M2 server)

The server (`src/StarshipMissionTracker.Api`) listens on `http://127.0.0.1:8080` and only on loopback. All JSON
responses use camelCase names. Times are UTC ISO 8601 strings ending in `Z`. A mission is addressed by its code
(`[A-Za-z0-9._-]{1,64}`, for example `demo-ift`), not by an internal id. Simulated data is labelled `Simulated`; it is
never telemetry. The database structure is in [DATAMODEL.md](DATAMODEL.md).

## Access rules

- **Read endpoints** (all `GET`) are open to any caller that reaches the server. The server itself only listens on
  loopback, and the framework `AllowedHosts` list (`localhost;127.0.0.1;[::1]`) answers `400` for any other `Host`.
- **Mutating endpoints** (`POST`, `PUT`) are loopback only. Checks, in this order:
  1. The caller's address must be loopback (IPv4 `127.x.x.x`, `::1`, or IPv4-mapped loopback). Otherwise `403`.
  2. The `Host` header must be a loopback IP literal or a name in the allow-list (`localhost`, `127.0.0.1`, `[::1]`,
     port ignored, case-insensitive). A missing or foreign `Host` gets `403`. This stops DNS rebinding (a hostile web
     page whose name resolves to 127.0.0.1). Extra names can only be added (`HostFilter:AllowedHostNames`, and also to
     `AllowedHosts`).
  3. `Content-Type` must be `application/json` (charset `utf-8` allowed). Otherwise `415`.
- The server sends no CORS headers, so a browser page from another origin cannot make these requests.
- Request bodies: one JSON object, at most 16 KB (`413` when larger), no duplicate property names, names are
  case-sensitive, unknown properties are ignored. A missing/empty body, invalid JSON, a non-object body, a wrong value
  type or a non-finite number gives `400`.

## Error format

JSON endpoints answer errors as `{"error":"<kind>","reason":"<readable text>"}`.

| Status | `error` | Meaning |
|---|---|---|
| 400 | `bad-request` | Invalid input (mission code, body, query value, speed range, countdown range, time format). |
| 403 | `forbidden` | Mutating call from a non-loopback caller or with a non-local `Host`. |
| 404 | `not-found` | Unknown mission, or no mission in the database. |
| 409 | `conflict` | The call does not fit the current simulation state. |
| 413 | `payload-too-large` | Body larger than 16 KB. |
| 415 | `unsupported-media-type` | `Content-Type` is not `application/json`. |

The `/kml/*` endpoints answer errors as plain text (see below).

## Simulation

Status JSON (returned by every simulation endpoint):

```json
{
  "state": "Running",
  "mission": "demo-ift",
  "speed": 10,
  "paused": false,
  "missionOffsetSeconds": 123.4,
  "launchTimeUtc": "2026-12-01T14:00:00Z",
  "reason": null,
  "speedMin": 0.1,
  "speedMax": 100,
  "dataQuality": "Simulated"
}
```

`state` is `Idle`, `Countdown`, `Running` or `Completed`; a paused run is `Running` with `paused: true`.
`missionOffsetSeconds` is T+ in seconds (negative during the countdown). `launchTimeUtc` is the T0 of the run (the
simulation anchor). `reason` explains why a run ended early (for example repeated failures), else `null`.

| Method, path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/simulation` | none | 200 status | none |
| `POST /api/simulation/start` | `{"mission":"demo-ift","speed":10,"countdownSeconds":10}` | 200 status (normally `Countdown`) | 400, 403, 404, 409, 415 |
| `POST /api/simulation/stop` | JSON object (e.g. `{}`) not read; `Content-Type` still required | 200 status | 403, 409 (nothing to stop), 415 |
| `PUT /api/simulation/speed` | `{"speed":2.5}` | 200 status | 400, 403, 409 (not in Countdown or Running), 415 |
| `POST /api/simulation/pause` | not read; `Content-Type` still required | 200 status | 403, 409 (not running, already paused, or still in the countdown), 415 |
| `POST /api/simulation/resume` | not read; `Content-Type` still required | 200 status | 403, 409 (not paused), 415 |

Start body: `mission` is required (mission code rule). `speed` is optional (default `Simulation:DefaultSpeed`, 10) and
must be a finite number within `[0.1, 100]`; it is never clamped. `countdownSeconds` is optional (default 10) within
`[0, 86400]`. Start errors: `400` invalid body or values, `404` unknown mission, `409` a simulation is already in
Countdown or Running, or the mission is not a simulated mission.

Starting a run sets a new simulation anchor for the mission and deletes the old simulated samples of that mission.
The planned T0 is never changed by a run. See [simulator.md](simulator.md).

## Missions

| Method, path | Response |
|---|---|
| `GET /api/missions` | 200, list of mission headers. |
| `GET /api/missions/current` | 200 mission header of the current mission; 404 when the database has no mission. |
| `GET /api/missions/{code}` | 200 mission header; 400 bad code; 404 unknown. |
| `GET /api/missions/{code}/trajectory` | 200, list of stored paths (one per vehicle path, source points, not densified); 400; 404. |
| `GET /api/missions/{code}/events` | 200, planned events ordered by offset; 400; 404. |
| `GET /api/missions/{code}/telemetry` | 200, stored telemetry samples; 400; 404. |
| `PUT /api/missions/{code}/launch-time` | 200 mission header after the change; 400, 403, 404, 409, 415. |

**Mission header** fields: `missionCode`, `name`, `description`, `status` (`Scheduled`, `InFlight`, `Completed`),
`launchTimeUtc`, `plannedLaunchTimeUtc`, `simulationLaunchTimeUtc`, `actualLaunchTimeUtc`, `isSimulated`,
`updatedAtUtc`, `launchSite` (`{latitude, longitude}` or null).

- `launchTimeUtc` is the **effective T0** every document uses: the simulation anchor if there is one, otherwise the
  planned T0. For a simulated mission it may be the simulation anchor, also after a run was stopped, so it is not a real
  launch time. `plannedLaunchTimeUtc` is the planned T0 (what `PUT launch-time` changes). Only `actualLaunchTimeUtc` of
  a mission with `isSimulated: false` is a real liftoff.

**Trajectory** item: `vehicle`, `vehicleName`, `type`, `quality`, `source`, `license`, `points[]` with
`offsetSeconds`, `timeUtc` (T0 + offset), `latitude`, `longitude`, `altitudeMeters` (MSL, see
[altitude-reference.md](altitude-reference.md)), optional `velocityMetersPerSecond`, `headingDegrees`,
`pitchDegrees`, `rollDegrees`, `quality`, `source`.

**Event** item: `type`, `name`, `description`, `offsetSeconds`, `timeUtc`, `vehicle`, `latitude`, `longitude`,
`altitudeMeters`, `quality`, `source`.

**Telemetry** query parameters (all optional, each at most once):

| Parameter | Rule | Default |
|---|---|---|
| `vehicle` | `Starship` or `SuperHeavy` (case-insensitive, names only). | all vehicles |
| `since` | UTC time ending in `Z`, inclusive. | from the start |
| `limit` | integer 1..10000. | 1000 |

A bad value gives `400`. Sample fields: `vehicle`, `timestampUtc`, `offsetSeconds` (T+ against the effective T0),
`receivedAtUtc`, `latitude`, `longitude`, `altitudeMeters`, `velocityMetersPerSecond`, `headingDegrees`,
`pitchDegrees`, `rollDegrees` (nullable), `quality`, `isEstimated` (true for every quality below Published; simulated
samples are never presented as measured), `source`.

**Change the planned T0:** `PUT /api/missions/{code}/launch-time` with `{"launchTimeUtc":"2026-12-01T23:30:00Z"}`
(strict UTC format ending in `Z`; must be in 2000-01-01T00:00:00Z inclusive .. 2200-01-01T00:00:00Z exclusive, otherwise
`400`). Answers `409` while a run of that mission is in Countdown or Running (paused
included); a Completed run or a run of another mission does not block it. On success, in one database transaction, the
planned T0 is set and the simulation anchor, the actual launch time and the simulated samples of the mission are reset.
The check and the write run under the simulation's operation lock, so a concurrent Start cannot slip in between.

## KML

| Method, path | Content |
|---|---|
| `GET /kml/master` | Master document with the three NetworkLinks. |
| `GET /kml/planned` | Planned trajectory, events, mission information. |
| `GET /kml/history` | Actual (simulated) trajectory so far. |
| `GET /kml/live` | Vehicles at their latest sample. |

All take an optional `mission=<code>` query parameter; without it the current mission is used. Other query parameters
(Google Earth appends the cookie `r=<n>`) are ignored. Success: `200`, `Content-Type:
application/vnd.google-earth.kml+xml; charset=utf-8`, UTF-8 without BOM, gzip when the client accepts it, and the
headers `Cache-Control: no-store, no-cache, must-revalidate, max-age=0`, `Pragma: no-cache`, `Expires: 0`, no ETag or
Last-Modified. Errors are plain text (`text/plain`, shown by Google Earth as the link error): `400` bad mission code,
`404` unknown mission or `No mission in the database.`. Document structure: [kml-server.md](kml-server.md).

The URLs inside the documents are built from the request's `Host`. A `Host` that is not a local name is not copied into
them: `127.0.0.1` with the connection's port is used (and a warning is logged); the request is still answered.

## Static content

| Path | Content |
|---|---|
| `GET /` and `GET /control` | 302 redirect to `/control/index.html`. |
| `GET /control/index.html` (+ `.js`, `.css`) | The simulation control page (no external resources). |
| `GET /assets/models/*`, `GET /assets/icons/*` | Placeholder 3D models (`.dae`, `model/vnd.collada+xml`) and icons from the repository's `assets/` folder; used by the KML. |
