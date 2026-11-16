---
name: kml-kmz
description: Read before writing, changing or testing anything that generates KML/KMZ for Google Earth Pro (coordinates, colors, gx:Track, Model, packaging, NetworkLink).
---

# KML 2.2 / gx / KMZ for Google Earth Pro

Project rules: `ai/KONVENCIOK.md` sections "KML / KMZ szabályok" and "Adatminőség". Reference: https://developers.google.com/kml/documentation/kmlreference

## Must-know details
- `<coordinates>`: `lon,lat,alt` separated by commas without spaces; tuples separated by whitespace. Lon FIRST. Swap from `Latitude/Longitude` happens in exactly one writer method, with a test.
- `gx:coord` inside `gx:Track` is different: space-separated `lon lat alt`, paired with a `<when>` per point (ISO 8601 UTC, `2026-01-01T12:00:00Z`). Counts of `when` and `gx:coord` must match, `when` ascending.
- Colors are `aabbggrr` (alpha, blue, green, red): blue = `ffff0000`, green = `ff00ff00`, orange = `ff0080ff`. All in `KmlStyles`.
- Number format: InvariantCulture, 6-7 decimals for lat/lon, 1 for altitude.
- `altitudeMode`: `absolute` (above MSL/EGM96), `relativeToGround`, `clampToGround`; for orbital/suborbital paths use `absolute`. `tessellate` has no effect with altitude; `extrude` draws a curtain to the ground (only when readable).
- `gx:altitudeMode` values (`relativeToSeaFloor`) live in the gx namespace; declare `xmlns:gx="http://www.google.com/kml/ext/2.2"` on `<kml>`.
- Model: `<Model><Location/><Orientation heading tilt roll/><Scale x y z/><Link><href>files/starship.dae</href></Link></Model>`. COLLADA is Z-up in Google Earth; check axes and scale (stack ~121 m) once, document in `docs/`. Texture paths in the `.dae` must be relative and also inside the KMZ.
- Time: `<TimeStamp>` for events, `<TimeSpan>` or `gx:Track` for replay (enables the time slider).
- Descriptions with HTML: wrap in `<![CDATA[ ]]>` or escape; user-visible strings in English. Escape `&`, `<` in names.

## Structure (spec 9.1)
Document > Folders: Mission Information, Planned Trajectory (Starship, Super Heavy), Actual Trajectory (...), Vehicles, Mission Events. Planned and actual are always separate objects. Use `<Style id>` + `<styleUrl>#id`, `StyleMap` for highlight.
Toggle layers (N-39a, Cli KMZ and server planned doc): Folder `id="curtain"` "Flight path curtain" right after Planned Trajectory (`CurtainGenerator`, same coordinates as the planned path, `extrude 1`, styles `curtain-<pathStyleId>`; the planned path line itself is `extrude 0`, actual paths stay extruded) and Folder `id="event-labels"` "Event labels" right after Mission Events (`EventLabelGenerator`, label-only `#event-label` placemarks, TimeStamp only in the KMZ). Both use `#toggle-folder` (`ListStyle checkHideChildren`); styles come from `KmlStyles.CreateToggleStyles()` appended after `CreateAll`. Event icons (`#event`) have LabelStyle scale 0. Keep the folder ids stable (GE keeps the checkbox state across refreshes by id). Mission Information balloon = mission HTML + `TimelineHtml.Create(events)` (T+ table). Views: always `MissionLookAt.Create(lat, lon, clock)` (`gx:TimeStamp` = T0 sets slider and Sun on open); KMZ Document + Mission Information placemark use `mission.Clock`, server master uses `new LaunchClock(info.LaunchTimeUtc)` (N-37 effective T0); child docs have no LookAt.
`ExtendedData` carries `DataQuality`, `SourceId`/source name. Estimated or simulated data never gets the measured (green) style. Planned line label: "Planned / Reconstructed – not live telemetry".

## KMZ
- Zip with `doc.kml` in the root (UTF-8, no BOM problems) and assets under `files/`. Use `System.IO.Compression.ZipArchive`, entries with forward slashes.
- Determinism: fixed entry order, fixed `LastWriteTime` (e.g. `DateTimeOffset(1980,1,1,...)`), no timestamps/GUIDs in output unless from input. Needed for golden-file tests.
- A `.kmz` is the same zip; test by reopening with `ZipFile.OpenRead` and checking `doc.kml` and `files/*` exist.

## Generating
- Prefer `System.Xml.Linq` (`XDocument`/`XNamespace`) over string concatenation: escaping and namespaces are correct. Templates in `Kml/Templates/` only for static fragments.
- The Kml module has no network and no DB; it takes models in, returns `string`/`byte[]`.

## NetworkLink (M2+, server KML in `Kml/Dynamic/`)
Master doc holds `NetworkLink`s to `http://127.0.0.1:8080/kml/...` (planned / history / live):
`<NetworkLink><flyToView>0</flyToView><refreshVisibility>0</refreshVisibility><Link><href>..</href><refreshMode>onInterval</refreshMode><refreshInterval>N</refreshInterval></Link></NetworkLink>`
- Always set `flyToView 0` (otherwise the camera jumps on every refresh) and `refreshVisibility 0` (otherwise the user's checkbox choices are reset on refresh).
- Live data: `onInterval`, >= 5 s (also enforce server side with `NetworkLinkControl/minRefreshPeriod`), or `onRequest`.
- Cache busting (N-15): every `/kml/*` response carries `Cache-Control: no-store` AND a `<NetworkLinkControl><cookie>rev=N</cookie></NetworkLinkControl>` (revision) before `<Document>`. Per the KML reference, `cookie` text is appended to the link's URL query on the next refresh, so each refresh URL is unique. Do not rely on only one of the two. (Reference semantics from memory of the KML docs; confirm the exact query shape in Google Earth Pro.)
- Content type `application/vnd.google-earth.kml+xml` (or `...kmz`). NetworkLinkControl only works in the fetched KML, not in the master.
- Live placemarks (current position, last-known point) carry NO `TimeStamp`/`TimeSpan`: with the time slider moved back, timed features vanish. Events of the server KML are also untimed (T0+offset in name/description).
- `Kml/Dynamic` generators take `nowUtc` and the revision as parameters; no clock, no DB, no HTTP.
- Assets over HTTP: the `Model` `Link href` and icons must be reachable from the document's base URL (e.g. `http://127.0.0.1:8080/assets/starship.dae`). RISK (unverified in this project): Google Earth Pro may fail to load a remote `.dae` or its relative textures from a NetworkLink. Fallback: serve a small KMZ (`doc.kml` + `files/`) from the endpoint, or keep the model in the locally opened master KMZ. Test manually and record in `docs/ge-ellenorzes.md`.
- Server is loopback only; Google Earth Pro runs on the same machine.

## Testing
- XML validity: parse with `XDocument.Parse`; optional XSD validation (OGC KML 2.2 + gx schema) if added to the repo.
- Assert coordinate order with a known point (e.g. Boca Chica ~ lat 25.997, lon -97.157 must appear as `-97.157...,25.997...`).
- Visual check in Google Earth Pro is manual (`docs/ge-ellenorzes.md`); never claim it passed without the user.
- Google Earth Pro caches NetworkLinks and styles; a stale picture may be cache, restart/clear before reporting a bug.
