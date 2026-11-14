# Asset licences and conventions

All assets in this repository are original work, generated procedurally by
`tools/assets/Generate-Assets.ps1` (PowerShell + System.Drawing, no third-party
data). They contain no SpaceX imagery, logos or models.

**Licence:** same as the repository licence – MIT (see [LICENSE](../LICENSE)).

Regenerate with: `powershell -ExecutionPolicy Bypass -File tools/assets/Generate-Assets.ps1`

## Assets

| File | What | Made how |
|---|---|---|
| `assets/models/starship.dae` | Placeholder Starship (COLLADA 1.4.1) | Script: 24-sided cylinder + cone, 4 box fins |
| `assets/models/superheavy.dae` | Placeholder Super Heavy booster (COLLADA 1.4.1) | Script: 24-sided cylinder + short cap, 4 box grid fins |
| `assets/icons/starship.png` | Navigation icon, 64x64 PNG, transparent | Script: white arrowhead with dark outline |
| `assets/icons/booster.png` | Navigation icon, 64x64 PNG, transparent | Script: orange capsule with dark outline (visibly different from the arrowhead) |

## Model conventions

- Units: metres (`<unit meter="1"/>`), `<up_axis>Z_UP</up_axis>`.
- Origin: centre of the base (z = 0 plane), on the vehicle axis.
- Long axis: +Z, nose/top pointing +Z. No textures; single-colour phong materials.
- Fins: Starship aft fins along +Y/-Y (z 0–8), forward flaps along +X/-X (z 33–40).
  Super Heavy grid fins at z 62–66 on the +/-X and +/-Y sides.

| Model | Diameter | Height | Notes |
|---|---|---|---|
| starship.dae | 9 m (r = 4.5) | 50 m | body z 0–40, nose cone z 40–50; fin span ~19 m aft / ~17 m forward |
| superheavy.dae | 9 m (r = 4.5) | 72 m | body z 0–70, cap z 70–72 |

## KML `<Orientation>` mapping

The model is placed with `<Model>` at `<Location>` (the base centre). With the
model's nose along +Z and Z_UP, a model with heading = 0, tilt = 0, roll = 0 would
stand vertically (nose up) in Google Earth.

- `heading` – rotation about the vertical axis, degrees clockwise from north.
- `tilt` – rotation about the model's X axis: 0 = nose straight up (+Z), 90 = nose
  horizontal (pitch 0 for a vehicle flying level), 180 = nose down. Pitch above the
  horizon = 90 - tilt.
- `roll` – rotation about the model's own Z (long) axis in this convention; 0 = no roll.

Note for the KML writer (N-related work): to show a vehicle with its nose pointing
along the heading at a pitch angle `p` above the horizon, write
`tilt = 90 - p`. This convention is to be confirmed visually in Google Earth Pro
(the placeholder models are meant for exactly that check); correct this section if
the check shows a different sign for roll or tilt.

## Third-party components

| Component | Version | Licence | Copyright | Source |
|---|---|---|---|---|
| GeographicLib.NET (WGS84 geodesics, NuGet package) | 2.3.2 | MIT | (c) Charles Karney and GeographicLib.NET contributors | https://github.com/noelex/GeographicLib.NET |
| Serilog (logging, NuGet package) | 4.3.0 | Apache-2.0 | (c) Serilog Contributors | https://github.com/serilog/serilog |
| Serilog.Sinks.Console (NuGet package) | 6.1.1 | Apache-2.0 | (c) Serilog Contributors | https://github.com/serilog/serilog-sinks-console |
| Serilog.Sinks.File (NuGet package) | 7.0.0 | Apache-2.0 | (c) Serilog Contributors | https://github.com/serilog/serilog-sinks-file |

GeographicLib.NET is used by `StarshipMissionTracker.Trajectory` for geodesic distance, azimuth and interpolation. The
Serilog packages are used only by `StarshipMissionTracker.Cli` (console + rolling file log). None is bundled as source;
they are restored from NuGet at build time.

The KML event marker style references Google Earth's built-in icon
`https://maps.google.com/mapfiles/kml/shapes/placemark_circle.png` by URL; it is not bundled in the repository or the KMZ.
Google Earth Pro needs internet access to draw it (offline the event icons are missing).

## Test-only packages

Used only by the projects under `tests/`; not part of the built tool or the KMZ.

| Component | Version | Licence | Source |
|---|---|---|---|
| xunit.v3 (test framework, NuGet package) | 4.0.3 | Apache-2.0 | https://github.com/xunit/xunit |
