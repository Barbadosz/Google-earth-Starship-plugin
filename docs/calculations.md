# Calculation assumptions

How positions, times and angles are calculated in the `StarshipMissionTracker.Trajectory` module and written to KML.
All values are approximations of the input data. They never become "measured" (see [data-sources.md](data-sources.md)).

## Geodesics (WGS84)

- Library: [GeographicLib.NET](https://github.com/noelex/GeographicLib.NET) (a C# port of GeographicLib by Charles
  Karney, MIT licence). Ellipsoid: WGS84.
- Distance, azimuth and intermediate points are **ellipsoidal geodesics**, not spherical great circles (the difference
  can reach about 0.5 %).
- Latitude and longitude are WGS84 geodetic degrees. Longitude is normalised to [-180, 180). The antimeridian is handled
  by the library.
- Ground distance is measured along the ellipsoid surface. Altitude is not part of it.

## Altitude reference

Altitude is **metres above mean sea level (EGM96 geoid)**, passed to KML `altitudeMode="absolute"` without change. No
geoid conversion is done in M1. Details, and what must be done if a source gives ellipsoidal height, are in
[altitude-reference.md](altitude-reference.md).

## Time model

- A point or event has only an offset `OffsetSeconds` from launch (T+, in seconds; before launch negative). There is no
  absolute time in the data.
- The launch time **T0** is `launchTimeUtc` in `data/missions/<code>.json` (ISO 8601 UTC, ending in `Z`).
- KML time = `T0 + OffsetSeconds`, in UTC, format `yyyy-MM-ddTHH:mm:ssZ` (milliseconds `...ss.fffZ` in `gx:Track` when
  needed). The time is calculated in one place (`LaunchClock`). The generator reads no clock, so the same input gives
  the same output.

### Override precedence

1. `--launch-time <ISO 8601 UTC>` on the command line, if given.
2. else `launchTimeUtc` in the mission file.
3. else an error. There is no default and no guess from the current time.

When the override is used, the value in the file is not even parsed. The demo file has the placeholder
`2099-01-01T00:00:00Z`.

## Densification

KML draws straight lines between points, which can cut through the ground on long legs. The path is therefore densified
along the WGS84 geodesic (`TrajectoryProcessor`, options `TrajectoryDensificationOptions`):

- Each segment between two source points is split into N equal parts (equal ground distance and equal time):
  `N = max(ceil(groundDistance / MaxSpacingMeters), ceil(timeGap / MaxSpacingSeconds))`.
  Either limit being exceeded splits the segment.
- Defaults: `MaxSpacingMeters` = 10 000 m, `MaxSpacingSeconds` = 10 s.
- The distance is measured along the ellipsoid surface. At high altitude the true 3D chord is a little longer (about
  2-3 % at 150 km), so the real spacing can slightly exceed the limit. A vertical-only segment is split by time only.
- Source points are kept unchanged. Inserted points get position from the geodesic, and altitude, velocity and
  offset linear. Their quality is at most `Estimated` (never better than their neighbours).
- Demo result: Starship 55 -> 1972 points, Super Heavy 34 -> 63 points.

## Interpolation

- Position at any offset (`TrajectoryInterpolator`): along the geodesic between the two neighbouring source points, with
  constant ground speed inside a segment. Altitude, velocity and offset are linear.
- No extrapolation: outside the first and last offset there is no result.
- **Heading** is interpolated along the shorter arc (across the 0/360 wrap) and normalised to [0, 360).
- **Roll** is interpolated along the **shortest arc** across the +-180 wrap (for example from 170 to -170 degrees goes
  through 180, not through 0) and normalised to [-180, 180).
- Pitch is interpolated linearly. Orientation is interpolated only if both ends have a value.
- Interpolated points have quality at most `Estimated`.

## Orientation (heading, pitch, roll)

The demo data has no attitude, so it is derived from the **direction of travel** (`OrientationCalculator`):

- **Heading** = geodesic azimuth of the segment to the next point, degrees clockwise from true north (equals KML
  `heading`). The last point uses the arrival azimuth of the last segment.
- **Pitch** = `atan2(altitude change, ground distance)` in degrees, positive when climbing, limited to [-90, 90].
- **Roll** = 0 (no source for it).
- Where a segment has no horizontal motion (vertical lift-off), the nearest defined value is used: the previous one, or
  for the first points the first later one. A segment with no motion at all has no angle of its own.
- Existing source angles are kept. A point that gets any derived angle is set to at most `Estimated`.
- These angles describe the path geometry. They are not the measured attitude of a vehicle.

### From angles to KML

The KML `gx:Track` carries `gx:angles` = `heading tilt roll`, with `tilt = 90 - pitch`. The model's nose is along +Z, so
tilt 0 means nose up and tilt 90 means nose horizontal (see [assets-licenses.md](assets-licenses.md), "KML Orientation
mapping"). The `Model` element has no own `Orientation` when the track has angles (it would rotate the model twice).
The sign convention is checked by eye in Google Earth Pro, see [ge-ellenorzes.md](ge-ellenorzes.md).

## Region and level of detail

The 3D model is drawn only when its Region covers at least `ModelMinLodPixels` = 2000 screen pixels (placeholder value
in `KmlLod`, to be tuned in GE Pro). The navigation icon has no Region and is always visible.
