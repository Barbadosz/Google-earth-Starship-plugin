---
name: efcore-sqlite
description: Read before any M2+ work on the Data module, EF Core 10 + SQLite entities, migrations, repositories, or the ASP.NET Core Minimal API wiring that uses them.
---

# EF Core + SQLite + Minimal API (M2 onwards)

Not needed in M1. Data model: spec chapter 7 (`alapleírás.md`). Entities live in `Core/Entities` (no EF reference); EF configuration is in `Data` via `IEntityTypeConfiguration<T>` (Fluent API), not attributes in `Core`.

## Setup
- Packages (versions in `Directory.Packages.props`, match EF to .NET 10): `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` (PrivateAssets all) in the startup project, `Microsoft.EntityFrameworkCore.Tools` optional. `dotnet-ef` is a local tool: `dotnet new tool-manifest`, `dotnet tool install dotnet-ef`.
- Migrations: `dotnet ef migrations add <Name> --project src/StarshipMissionTracker.Data --startup-project src/StarshipMissionTracker.Api`. Only one migration package at a time (hot spot); never edit an applied migration, add a new one. Review the generated SQL/`Up`.
- Connection string from configuration (`ConnectionStrings:Default`, `Data Source=data/starship.db` relative), db file in git-ignore.

## SQLite specifics
- No native `DateTimeOffset` ordering/comparison in queries and no `decimal` aggregates: store UTC `DateTime` (or ticks/ISO text via value converter) and `double` for coordinates/altitude. Test the converter.
- Limited `ALTER TABLE`: some migrations rebuild the table; big data changes need care.
- Enable foreign keys (EF Core does by default with the SQLite provider) and add indexes on `(MissionId, Timestamp)` for telemetry/trajectory points.
- Store enums as strings (`HasConversion<string>()`) for `DataQuality`, `EventType`, so the DB stays readable.
- Bulk inserts of telemetry: `AddRange` + a single `SaveChanges` (or `ExecuteSql` for huge sets); do not call `SaveChanges` per point.
- Raw source payloads are stored unchanged (provenance); processed rows reference `SourceId`.

## Repository pattern
- Interfaces in `Core/Interfaces`, implementations in `Data`. `Api` and `Kml` never use `DbContext`. Return domain models, not tracked entities, for read paths; `AsNoTracking()` on reads.
- Async everywhere with `CancellationToken`, `Async` suffix.

## Minimal API
- `builder.Services.AddDbContext<StarshipDbContext>(...)`, endpoints grouped with `MapGroup("/api/missions")`, typed results (`TypedResults.Ok/NotFound`), `AddOpenApi()` (built in since .NET 9; Swashbuckle is no longer the default).
- KML endpoints return `Results.File(bytes, "application/vnd.google-earth.kmz", "mission.kmz")`.
- `Program.cs` is a hot spot (DI, endpoints): changed only in the wiring package.

## Tests
- Repository tests with SQLite in-memory: keep one open `SqliteConnection("DataSource=:memory:")` for the test lifetime (the DB vanishes when it closes), `EnsureCreated()` or apply migrations. Do not use the EF InMemory provider (different semantics).
- API tests: `WebApplicationFactory<Program>` (needs `public partial class Program {}` or `InternalsVisibleTo`).
