# Data model

This document describes the SQLite database as the EF Core 10 model and the migrations define it. It covers structure and the meaning of the time and provenance columns. It contains no data.

- **Database:** SQLite, EF Core 10 (`Microsoft.EntityFrameworkCore.Sqlite`)
- **File:** `data/starship.db`, relative to the repository root (configured as `Paths:Database` in the API settings). The `.db`, `.db-wal` and `.db-shm` files are git-ignored. Deleting the file and restarting re-creates and re-seeds it from `data/missions/`.
- **Last full sync:** 2026-11-16 (source: entity configurations and migration files in `src/StarshipMissionTracker.Data/`)

## Startup behaviour

When the API host starts, before the simulator runs:

1. `DatabaseInitializer` runs `PRAGMA journal_mode=WAL;` (persisted in the file; the simulator writes while the API reads), then `Database.MigrateAsync()` applies pending migrations.
2. The mission import reads `data/missions/*.json`. A mission whose code already exists in `Missions` is skipped. After the first import the database is the source of truth for T0.

The design-time factory (`StarshipDesignTimeFactory`) is used only by `dotnet ef`. It never opens the database.

## Overview

| Table | Purpose | Key relations |
|---|---|---|
| `Missions` | One row per mission: code, status, planned T0, actual liftoff, simulation anchor | parent of `Vehicles`, `MissionEvents`, `Trajectories` |
| `Vehicles` | One row per vehicle type of a mission (Starship, SuperHeavy) | → `Missions.Id` (cascade); parent of `Trajectories`, `TelemetrySamples` |
| `DataSources` | Registry of data origins (planned file, internal simulator) with the best quality each may claim | parent of `Trajectories`, `TelemetrySamples` (restrict) |
| `Trajectories` | One stored flight path (planned) per vehicle and type | → `Missions.Id`, `Vehicles.Id` (cascade), → `DataSources.Id` (restrict) |
| `TrajectoryPoints` | Points of a stored path, keyed by offset from T0 | → `Trajectories.Id` (cascade) |
| `TelemetrySamples` | Position samples of a vehicle with timestamp, quality and source (actual flight data, simulated in M2) | → `Vehicles.Id` (cascade), → `DataSources.Id` (restrict) |
| `MissionEvents` | Mission events (liftoff, max-Q, ...) with offset from T0 and optional position | → `Missions.Id` (cascade), → `Vehicles.Id` (set null) |

## Conventions

- **Keys:** `Guid` keys are version-7 GUIDs (time-ordered). `TrajectoryPoints.Id` and `TelemetrySamples.Id` are `INTEGER` autoincrement.
- **Types in SQLite:** `Guid` and `string` are `TEXT`; `double` is `REAL`; `bool` is `INTEGER` (0/1); instants (`DateTimeOffset`) are `INTEGER`.
- **Instants:** stored as UTC ticks (`UtcInstantConverter`, 100 ns resolution, so a fractional-second T0 survives). Reads return an offset of zero. All comparisons and ordering are on the integer.
- **Enums:** stored as their names in `TEXT` (for example `InFlight`), so provenance is readable in the file.
- **Foreign keys** are enforced. Delete behaviour is given per relation below.

## Time semantics

Three instants on `Missions` define the time axis. They must not be mixed up.

| Column | Meaning | Written by |
|---|---|---|
| `PlannedLaunchTimeUtc` | The planned T0 (the real plan for real missions). | Import (from the mission file's `launchTimeUtc`), and the T0 change operation only. The simulator never writes it. |
| `SimulationLaunchTimeUtc` | Simulation anchor: the wall-clock launch time of the latest simulated run. The timestamps of its stored samples are on this axis. Null for missions that never had a simulated run, and after a T0 change. | Start of a simulated run (kept on stop and on completion). Cleared by the T0 change operation. |
| `ActualLaunchTimeUtc` | Actual liftoff of the current run. Null during the countdown and after stop. | Simulation status changes to InFlight and Completed. Cleared on start and on stop. |

**Effective T0.** Every reader that converts a timestamp into T+ (seconds since T0) uses one value: `SimulationLaunchTimeUtc` if it is set, otherwise `PlannedLaunchTimeUtc`. This is `MissionInfo.LaunchTimeUtc` in Core. The history query (`TelemetryRepository.GetHistoryAsync`) and the mapping to `Mission` (`MissionMapper.ToMission`) apply the same rule. The planned T0 is therefore never overwritten by a simulation: a simulated run only adds its own anchor, and a crash or restart leaves the planned value intact.

**T0 change** (`SetPlannedLaunchTimeAsync`) runs in one transaction. It sets `PlannedLaunchTimeUtc`, clears `SimulationLaunchTimeUtc` and `ActualLaunchTimeUtc`, sets the status to `Scheduled`, and deletes the mission's `Simulated` samples. Non-simulated samples are never deleted by this operation.

**Simulated run start** (`BeginSimulationRunAsync`) runs in one transaction. It sets the status to `Scheduled`, sets the new anchor, clears `ActualLaunchTimeUtc`, and deletes the mission's `Simulated` samples. The planned T0 is not written.

## Launch site (derived, not stored)

There is no launch-site column. `MissionRepository` derives it per mission: the position (`Latitude`, `Longitude`) of the `MissionEvents` row with `Type = Liftoff`, non-null latitude and longitude, and the lowest `OffsetSeconds`. The result is passed as `MissionInfo.LaunchSite`. If the mission has no positioned liftoff event, `LaunchSite` is null, and the KML layer falls back to the first point of the first path.

## Provenance and quality

Every telemetry sample and every trajectory point carries a `Quality` (`DataQuality`) and a source name. Samples also carry a `SourceId` to `DataSources` and an `IsEstimated` flag.

`DataQuality` values, in increasing order of reliability: `Unknown`, `Simulated`, `Estimated`, `Reconstructed`, `Published`, `Measured`.

Rules enforced by the write path (`TelemetryRepository.AddRangeAsync`). A batch is checked completely before anything is saved, so a bad batch stores nothing.

- A source must be registered in `DataSources`. The only source created on the fly is the internal simulator source (`Core.Models.SimulatorSource.Name`).
- A simulator source yields only `Simulated` samples.
- A sample's quality may not be better than its source's `Reliability`.
- A mission with `IsSimulated = true` may not hold `Measured` samples.

`DataSources.Reliability` is the best quality the source itself can claim. `Unknown` means it varies per row.

**Simulated vs other samples.**

- *Simulated*: `Quality = Simulated` and the source is the internal simulator. These are the only telemetry rows the M2 simulator writes. They are placed on the simulation anchor's time axis and are deleted when a new run starts, when the T0 changes, or by `DeleteSimulatedAsync`.
- *Other*: `Estimated`, `Reconstructed`, `Published` or `Measured` rows from a registered source. The M2 code has no producer of these. The write path accepts them, subject to the rules above, and they are never deleted by run start or T0 change.

`Missions.IsSimulated` is set at import: true when every imported path has quality `Simulated`.

## Tables

### Table: Missions

One mission (a launch campaign such as one flight test). The mission code is the business key.

| Column | Type | Null | Note |
|---|---|---|---|
| Id | TEXT | no | PK, version-7 GUID |
| MissionCode | TEXT | no | Unique (`IX_Missions_MissionCode`) |
| Name | TEXT | no | |
| Description | TEXT | yes | |
| Status | TEXT | no | `Scheduled`, `InFlight`, `Completed` |
| PlannedLaunchTimeUtc | INTEGER | no | Planned T0, UTC ticks. See Time semantics. |
| ActualLaunchTimeUtc | INTEGER | yes | Actual liftoff of the current run |
| SimulationLaunchTimeUtc | INTEGER | yes | Simulation anchor. Added by migration `AddSimulationLaunchTime`. |
| IsSimulated | INTEGER | no | True when all imported paths are `Simulated` |
| CreatedAtUtc | INTEGER | no | Set at import |
| UpdatedAtUtc | INTEGER | no | Set on every write to the mission row |

- **Relations:** parent of `Vehicles` (cascade delete), `MissionEvents` (cascade delete), `Trajectories` (cascade delete, via `Trajectories.MissionId`).
- **Unique / index:** `IX_Missions_MissionCode` (unique).

### Table: Vehicles

One vehicle of a mission. A mission has at most one vehicle of each type.

| Column | Type | Null | Note |
|---|---|---|---|
| Id | TEXT | no | PK |
| MissionId | TEXT | no | FK → `Missions.Id`, cascade |
| Name | TEXT | no | |
| Type | TEXT | no | `Starship` or `SuperHeavy` |
| ModelPath | TEXT | yes | Path of the 3D model, if any |

- **Relations:** → `Missions.Id` (cascade). Parent of `Trajectories` and `TelemetrySamples` (cascade).
- **Unique / index:** `IX_Vehicles_MissionId_Type` (unique on `MissionId, Type`; also serves lookups by `MissionId`).

### Table: DataSources

Registry of data origins. Referenced by samples and trajectories.

| Column | Type | Null | Note |
|---|---|---|---|
| Id | TEXT | no | PK |
| Name | TEXT | no | Unique. Imported planned file: `Planned trajectory file`. Simulator: the name of `Core.Models.SimulatorSource`. |
| Type | TEXT | no | `PlannedFile` or `Simulator` |
| Url | TEXT | yes | Column exists; not set by the current import or simulator code |
| License | TEXT | yes | Column exists; not set by the current import or simulator code |
| Reliability | TEXT | no | Best `DataQuality` the source may claim |

- **Relations:** parent of `Trajectories.SourceId` and `TelemetrySamples.SourceId` (both restrict: a source in use cannot be deleted).
- **Unique / index:** `IX_DataSources_Name` (unique).

### Table: Trajectories

One stored flight path of one vehicle (planned, or reconstructed). Actual paths are not stored here; they are built from `TelemetrySamples`. The mapper refuses to store an `Actual` path.

| Column | Type | Null | Note |
|---|---|---|---|
| Id | TEXT | no | PK |
| MissionId | TEXT | no | FK → `Missions.Id`, cascade. Denormalised: the database does not check it equals `Vehicles.MissionId`; the mapper sets it from the same mission. |
| VehicleId | TEXT | no | FK → `Vehicles.Id`, cascade |
| Type | TEXT | no | `Planned`, `Actual`, `Reconstructed` (`Actual` is refused on write) |
| Quality | TEXT | no | `DataQuality` of the path |
| SourceId | TEXT | no | FK → `DataSources.Id`, restrict |
| SourceName | TEXT | no | Copy of the source name, kept with the path |
| License | TEXT | yes | |
| CreatedAtUtc | INTEGER | no | Set at import |

- **Relations:** → `Missions.Id` (cascade), → `Vehicles.Id` (cascade), → `DataSources.Id` (restrict). Parent of `TrajectoryPoints` (cascade).
- **Unique / index:** `IX_Trajectories_VehicleId_Type` (unique: one path of each type per vehicle), `IX_Trajectories_MissionId`, `IX_Trajectories_SourceId`.

### Table: TrajectoryPoints

Points of a stored path.

| Column | Type | Null | Note |
|---|---|---|---|
| Id | INTEGER | no | PK, autoincrement |
| TrajectoryId | TEXT | no | FK → `Trajectories.Id`, cascade |
| OffsetSeconds | REAL | no | Seconds from T0 (negative before T0) |
| Latitude | REAL | no | Degrees |
| Longitude | REAL | no | Degrees |
| AltitudeMeters | REAL | no | Metres |
| VelocityMps | REAL | yes | Metres per second; null when unknown |
| HeadingDegrees | REAL | yes | |
| PitchDegrees | REAL | yes | |
| RollDegrees | REAL | yes | |
| Quality | TEXT | no | `DataQuality` of the point |
| SourceName | TEXT | no | Source name of the point |

- **Relations:** → `Trajectories.Id` (cascade).
- **Unique / index:** `IX_TrajectoryPoints_TrajectoryId_OffsetSeconds` (unique: one point per offset per path; also serves lookups by `TrajectoryId`).

### Table: TelemetrySamples

Position samples of a vehicle. Mission membership is reached through `Vehicles`; there is no `MissionId` column. In M2 these rows are simulated.

| Column | Type | Null | Note |
|---|---|---|---|
| Id | INTEGER | no | PK, autoincrement |
| VehicleId | TEXT | no | FK → `Vehicles.Id`, cascade |
| TimestampUtc | INTEGER | no | Instant of the sample, UTC ticks. On the simulation anchor's axis for simulated samples. |
| ReceivedAtUtc | INTEGER | no | Instant the sample was received (as given by the sample) |
| Latitude | REAL | yes | Degrees; null when not known |
| Longitude | REAL | yes | Degrees |
| AltitudeMeters | REAL | yes | |
| VelocityMps | REAL | yes | |
| HeadingDegrees | REAL | yes | |
| PitchDegrees | REAL | yes | |
| RollDegrees | REAL | yes | |
| Quality | TEXT | no | `DataQuality` of the sample |
| IsEstimated | INTEGER | no | True when the value is estimated (flag as given by the sample; the write path does not derive it) |
| SourceId | TEXT | no | FK → `DataSources.Id`, restrict |

- **Relations:** → `Vehicles.Id` (cascade), → `DataSources.Id` (restrict).
- **Unique / index:** `IX_TelemetrySamples_VehicleId_TimestampUtc` (unique: one sample per vehicle and instant; also serves per-vehicle time-range reads), `IX_TelemetrySamples_SourceId`.

### Table: MissionEvents

Events of a mission (for example `Liftoff`, `MaxQ`, `Meco`, `Reentry`). The offset is from T0. The position is optional.

| Column | Type | Null | Note |
|---|---|---|---|
| Id | TEXT | no | PK |
| MissionId | TEXT | no | FK → `Missions.Id`, cascade |
| VehicleId | TEXT | yes | FK → `Vehicles.Id`, set null |
| Type | TEXT | no | `EventType` (for example `Liftoff`, `MaxQ`, `Meco`, `Reentry`, `SplashdownOrLanding`) |
| OffsetSeconds | REAL | no | Seconds from T0 |
| Name | TEXT | no | |
| Description | TEXT | yes | |
| Latitude | REAL | yes | Degrees; with `Longitude`, this is the position used for the launch site when `Type = Liftoff` |
| Longitude | REAL | yes | Degrees |
| AltitudeMeters | REAL | yes | |
| Quality | TEXT | no | `DataQuality` of the event |
| SourceName | TEXT | no | |

- **Relations:** → `Missions.Id` (cascade), → `Vehicles.Id` (set null: deleting a vehicle keeps the event).
- **Unique / index:** `IX_MissionEvents_MissionId_OffsetSeconds` (non-unique; events of a mission ordered by offset), `IX_MissionEvents_VehicleId`.

## Relationship summary

| Parent | Child | Delete behaviour |
|---|---|---|
| Missions | Vehicles | cascade |
| Missions | MissionEvents | cascade |
| Missions | Trajectories | cascade |
| Vehicles | Trajectories | cascade |
| Vehicles | TelemetrySamples | cascade |
| Vehicles | MissionEvents | set null |
| Trajectories | TrajectoryPoints | cascade |
| DataSources | Trajectories | restrict |
| DataSources | TelemetrySamples | restrict |

## Enums

Stored as `TEXT` names.

- `MissionStatus`: `Scheduled`, `InFlight`, `Completed`
- `VehicleType`: `Starship`, `SuperHeavy`
- `DataSourceType`: `PlannedFile`, `Simulator`
- `TrajectoryType`: `Planned`, `Actual`, `Reconstructed`
- `DataQuality`: `Unknown`, `Simulated`, `Estimated`, `Reconstructed`, `Published`, `Measured`
- `EventType`: `Liftoff`, `MaxQ`, `Meco`, `StageSeparation`, `BoosterBoostback`, `BoosterLandingBurn`, `BoosterLanding`, `StarshipEngineCutoff`, `Coast`, `Reentry`, `LandingBurn`, `SplashdownOrLanding`

## Migrations

Applied in this order. The migration files are in `src/StarshipMissionTracker.Data/Migrations/`; the model snapshot is `StarshipDbContextModelSnapshot.cs`.

1. `20261115134129_InitialCreate`: creates the tables `DataSources`, `Missions`, `Vehicles`, `MissionEvents`, `TelemetrySamples`, `Trajectories`, `TrajectoryPoints` with their keys, foreign keys and indexes.
2. `20261116082747_AddSimulationLaunchTime`: adds `Missions.SimulationLaunchTimeUtc` (`INTEGER`, nullable). Existing rows get null, so existing missions use `PlannedLaunchTimeUtc` as their T0 until their next simulated run.

Adding a migration: `dotnet ef migrations add <Name> --project src/StarshipMissionTracker.Data` (the design-time factory makes the startup project unnecessary). Migrations are applied at API startup, not by a separate command. Do not edit a migration that has already been applied to a database.

## Changelog

- 2026-11-16: document created from the EF Core configurations and the two migrations.
