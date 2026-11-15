---
name: efcore-sqlite
description: Read before any M2+ work on the Data module, EF Core 10 + SQLite entities, migrations, repositories, or the ASP.NET Core Minimal API wiring that uses them.
---

# EF Core 10 + SQLite + Minimal API (M2 onwards)

Not needed in M1. Data model: spec chapter 7 (`alapleírás.md`) + `ai/tervezo/terv-M2.md` section 7. Doc: `docs/DATAMODEL.md`.

## Layout (differs from the spec)
- Persistence entities are plain POCOs in `Data/Entities/` (no EF attributes). They are NOT in `Core/Entities`: Core's validating records are not EF-friendly, and Core must not reference EF.
- Mapping `Data/Mapping/` entity <-> Core model; repositories in `Data/Repositories/` implement `Core/Interfaces`. `Api`/`Telemetry`/`Kml` never see `DbContext`.
- Fluent config in `Data/Configurations/` (`IEntityTypeConfiguration<T>`, `ApplyConfigurationsFromAssembly`).
- Import/seed in `Data/Import/` (mission JSON -> DB on first run; afterwards the DB is the truth for T0).

## Setup
- Packages (versions in `Directory.Packages.props`, EF 10.x): `Microsoft.EntityFrameworkCore.Sqlite` in `Data`; `Microsoft.EntityFrameworkCore.Design` (PrivateAssets all) in the startup project. `dotnet-ef` is a local tool (`dotnet new tool-manifest`, `dotnet tool install dotnet-ef`).
- Design-time factory: `IDesignTimeDbContextFactory<StarshipDbContext>` in `Data`, so migrations work without starting the web host (and without the BackgroundService). Migrations: `dotnet ef migrations add <Name> --project src/StarshipMissionTracker.Data` (add `--startup-project src/StarshipMissionTracker.Api` only if the factory is absent).
- Migrations are a HOT SPOT: one package/agent at a time; never edit an applied migration; review the generated `Up`. Apply at startup via `Database.MigrateAsync()` in a scoped service (log the result).
- Connection string `ConnectionStrings:Default` = `Data Source=data/starship.db`, resolved against the repo root (not the CWD); `*.db`, `*.db-wal`, `*.db-shm` in `.gitignore`. Re-seed = delete the db.

## SQLite specifics
- WAL: `PRAGMA journal_mode=WAL;` once at startup (it persists in the db file). Whether `Journal Mode=WAL` works in the Microsoft.Data.Sqlite connection string is unverified: use the PRAGMA. Also `busy_timeout` because the simulator writes while the API reads.
- UTC `DateTime`: SQLite stores TEXT and EF returns `Kind=Unspecified`. Add a `ValueConverter<DateTime,DateTime>` (to UTC on write, `SpecifyKind(Utc)` on read), applied globally (`ConfigureConventions`) or per property. Keep the fractional second of T0. Test the round trip with sub-second values. Avoid `DateTimeOffset` in queries (ordering/comparison unsupported/slow).
- `double` for coordinates/altitude; no `decimal` aggregates.
- Enums as strings (`HasConversion<string>()`): `DataQuality`, `EventType`, `VehicleType`, `TrajectoryType`.
- Limited `ALTER TABLE`: EF rebuilds tables in migrations; care with data.
- Index `(MissionId, Timestamp)` / `(MissionId, OffsetSeconds)` on `TelemetrySamples`; foreign keys on by default.
- Bulk: `AddRange` + one `SaveChanges`; never per point.
- Provenance: raw source payloads unchanged; processed rows keep `SourceId`, `DataQuality`.

## Repository pattern
- `AsNoTracking()` on reads, return Core models, async + `CancellationToken`. Register `DbContext` scoped; the `BackgroundService` (singleton) must create a scope (`IServiceScopeFactory`) or use `IDbContextFactory<T>`; never inject `DbContext` into it.
- Log (`ILogger<T>`): migration/import result, T0 change old -> new. EF command logging only from Warning.

## Minimal API
- `MapGroup("/api/missions")`, `TypedResults`, `AddOpenApi()` (built in; no Swashbuckle). Mutating endpoints (T0, simulation start/stop): loopback caller only, strict parsing at the boundary.
- KML endpoints return KML text with `application/vnd.google-earth.kml+xml` and `Cache-Control: no-store` (see `kml-kmz`).
- `Program.cs` only wires (hot spot, wiring package); end it with `public partial class Program { }` for tests.

## Tests
- `Data.Tests`: one open `SqliteConnection("DataSource=:memory:")` for the test lifetime; apply migrations (catches migration bugs) or `EnsureCreated`. EF InMemory provider is forbidden.
- Api integration tests use a temp SQLite file (see `dotnet-csharp`).
