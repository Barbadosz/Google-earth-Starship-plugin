---
name: geodesy-trajectory
description: Read before writing trajectory calculation, interpolation, GeographicLib use, height reference conversion or related tests in the Trajectory module.
---

# Geodesy and trajectory (Trajectory module)

## Library
- GeographicLib (Karney) C# port from NuGet. Check the exact package id and API on nuget.org before adding (several ports exist: `GeographicLib`, `GeographicLib.NET`); pin it in `Directory.Packages.props`. Used only inside `Trajectory`.
- Typical use: `Geodesic.WGS84.Inverse(lat1, lon1, lat2, lon2, out distanceMeters, out azi1, out azi2)`, `Direct(...)` for point at distance/azimuth. Angles in degrees, lengths in meters, azimuth clockwise from north (= KML heading).
- Geocentric/local conversions: `Geocentric.WGS84.Forward/Reverse`, `LocalCartesian` for ENU. Geoid heights: `Geoid` class needs EGM96/EGM2008 data files (not in the NuGet package); decide with the user where they live (`data/`), do not commit large grids silently.

## Conventions
- Internal names `Latitude`, `Longitude`, `AltitudeMeters` (degrees, WGS84). Never store lon/lat swapped; swap only in the KML writer.
- Longitude range -180..180; normalize after calculations. Do not assume the path is short: great-circle (geodesic) interpolation, not linear lat/lon interpolation.
- Height reference: GeographicLib/GPS give ellipsoidal height; KML `absolute` expects height above MSL (EGM96). Conversion `h_msl = h_ellipsoid - N(lat,lon)`; N is up to tens of meters. Record which reference every data set uses, document in `docs/`.
- Interpolation: along time for position (geodesic for horizontal, linear for altitude unless the data justify spline), result points get `DataQuality.Estimated`, `IsEstimated = true`. Never extrapolate past the last source point without marking it.
- Speed/heading derived from positions: heading from `Inverse` azimuth, speed = distance / dt (check dt > 0, strictly ascending timestamps; reject unsorted input).
- The M1 sample trajectory is approximate (Boca Chica launch, Gulf of Mexico/Florida Straits/Atlantic style profile): label it approximate in KMZ and docs, source `Simulated`/`Reconstructed`, no invented times presented as fact.

## Tests
- Reference values with source: GeographicLib test data / Karney's examples (e.g. JFK -> LHR style inverse examples), or hand-checked values with tolerance (1e-6 deg, 1 m, not exact equality on doubles).
- Edge cases: antimeridian crossing, poles, identical points (distance 0, azimuth undefined), single-point list, duplicate timestamps.

## Mistakes to avoid
- `Math.Sin` etc. take radians; GeographicLib takes degrees.
- Spherical haversine instead of ellipsoidal: differences up to ~0.5%, fails reference tests.
- Mixing nautical miles / feet (spec sources often use them) with meters: convert at the input boundary and keep the raw value in `data/`.
