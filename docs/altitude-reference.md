# Altitude reference and trajectory calculation assumptions

## Altitude reference (M1)

- `TrajectoryPoint.AltitudeMeters` and `MissionEvent` altitudes are **metres above mean sea level (MSL), EGM96 geoid**.
- This matches KML `altitudeMode="absolute"`, which Google Earth interprets as height above the EGM96 geoid.
- **M1 assumption:** all M1 input data (the approximate planned trajectories in `data/trajectories/`) is already
  given as height above MSL. No geoid conversion is performed in M1; altitudes are passed to KML unchanged.
- Latitude/longitude are WGS84 geodetic coordinates in degrees. Longitude is normalised to [-180, 180)
  (+180 is stored as -180).

## When a geoid conversion becomes necessary

GPS receivers, orbital state vectors and GeographicLib's own geocentric conversions give **ellipsoidal height**
(above the WGS84 ellipsoid). The relation is

    h_msl = h_ellipsoid - N(lat, lon)

where `N` is the geoid undulation (roughly -106 m to +85 m worldwide; around -25 m in the Gulf of Mexico).
If a future data source (for example live telemetry in M2 or reconstructed GNSS tracks) provides ellipsoidal heights:

1. The conversion is done in the `Trajectory` module, at the input boundary, before `TrajectoryPoint` objects are
   built (the Core model always holds MSL).
2. GeographicLib's `Geoid` class needs the EGM96 grid files (for example `egm96-5.pgm`). These are **not** part of
   the GeographicLib.NET NuGet package. They are distributed separately by the GeographicLib project
   (geographiclib.sourceforge.io, "Geoid data"), and their licence must be checked and recorded in
   `docs/assets-licenses.md` before use.
3. Proposed location: `data/geoids/` (not committed if large; the README would describe how to download them).
4. The data source and its height reference are recorded for every data set (data file `source` field and this
   document).

## Geodesic calculations

- Library: [GeographicLib.NET](https://github.com/noelex/GeographicLib.NET) (NuGet `GeographicLib.NET`, MIT
  licence), a C# port of Charles Karney's GeographicLib. Ellipsoid: WGS84.
- Distances are ellipsoidal (geodesic) ground distances, not spherical great-circle approximations
  (the difference can reach about 0.5 %).
- Crossing the antimeridian (±180°) is handled by the library (it works with longitude differences); every
  computed longitude is normalised to [-180, 180).

## Densification

KML draws straight segments between points, which can cut below the Earth's surface on long legs. The
`TrajectoryProcessor` therefore inserts points along the WGS84 geodesic:

- A segment is split into N equal parts (equal ground distance and equal time) with
  `N = max(ceil(groundDistance / MaxSpacingMeters), ceil(timeGap / MaxSpacingSeconds))`, i.e. whenever **either**
  limit is exceeded. Defaults: 10 000 m and 10 s (`TrajectoryDensificationOptions.Default`).
- The distance limit uses the horizontal distance measured along the ellipsoid surface (the geodesic at sea level
  height), not the straight chord between the 3D points. At 150 km altitude the chord is roughly 2-3 % longer than
  the surface distance, so the real 3D spacing can slightly exceed the limit. A vertical-only segment is split by time only.
- Inserted points: position along the geodesic, altitude, velocity and offset interpolated linearly, orientation
  interpolated only when both ends have it. Quality is `Estimated` (or the worse source quality, never better).
- Original points are kept unchanged. Because a path's quality may not be better than its worst point, a densified
  flight path has quality `Estimated` (or worse).

## Interpolation and orientation

- Position at an arbitrary T+ offset: geodesic between the neighbouring source points (constant ground speed within
  a segment), altitude linear. No extrapolation before the first or after the last point. Result quality `Estimated`.
- Derived orientation (where the source has none): heading = geodesic azimuth of travel (degrees clockwise from true
  north), pitch = `atan2(altitude change, ground distance)`, roll = 0. These describe the path geometry, not a
  measured vehicle attitude. Where a segment has no horizontal motion (vertical lift-off), the heading of the nearest
  moving segment is reused.
