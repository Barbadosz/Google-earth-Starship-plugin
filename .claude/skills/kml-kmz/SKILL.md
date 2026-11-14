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
`ExtendedData` carries `DataQuality`, `SourceId`/source name. Estimated or simulated data never gets the measured (green) style. Planned line label: "Planned / Reconstructed – not live telemetry".

## KMZ
- Zip with `doc.kml` in the root (UTF-8, no BOM problems) and assets under `files/`. Use `System.IO.Compression.ZipArchive`, entries with forward slashes.
- Determinism: fixed entry order, fixed `LastWriteTime` (e.g. `DateTimeOffset(1980,1,1,...)`), no timestamps/GUIDs in output unless from input. Needed for golden-file tests.
- A `.kmz` is the same zip; test by reopening with `ZipFile.OpenRead` and checking `doc.kml` and `files/*` exist.

## Generating
- Prefer `System.Xml.Linq` (`XDocument`/`XNamespace`) over string concatenation: escaping and namespaces are correct. Templates in `Kml/Templates/` only for static fragments.
- The Kml module has no network and no DB; it takes models in, returns `string`/`byte[]`.

## NetworkLink (M2+)
`<NetworkLink><Link><href>http://localhost:.../kml/...</href><refreshMode>onInterval</refreshMode><refreshInterval>N</refreshInterval>` ; for live data use `onInterval` with a sensible minimum (>= 5 s), or `onRequest`. Server must return `application/vnd.google-earth.kml+xml` / `...kmz`.

## Testing
- XML validity: parse with `XDocument.Parse`; optional XSD validation (OGC KML 2.2 + gx schema) if added to the repo.
- Assert coordinate order with a known point (e.g. Boca Chica ~ lat 25.997, lon -97.157 must appear as `-97.157...,25.997...`).
- Visual check in Google Earth Pro is manual (`docs/ge-ellenorzes.md`); never claim it passed without the user.
- Google Earth Pro caches NetworkLinks and styles; a stale picture may be cache, restart/clear before reporting a bug.
