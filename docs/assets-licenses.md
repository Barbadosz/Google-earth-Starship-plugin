# Asset licences and conventions

All assets in this repository are original work, generated procedurally by
`tools/assets/Generate-Assets.ps1` (PowerShell + System.Drawing, no third-party
data). They contain no SpaceX imagery, logos or models.

**Licence:** same as repository licence – LICENSE file pending (the repository
has no LICENSE file yet; this must be added before the repo is made public).

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
