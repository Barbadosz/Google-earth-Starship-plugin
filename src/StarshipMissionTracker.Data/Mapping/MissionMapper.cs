using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Mapping;

/// <summary>Mission entity graph &lt;-&gt; Core models. The Core constructors re-validate everything read from the database.</summary>
internal static class MissionMapper
{
    public static MissionInfo ToInfo(MissionEntity e) =>
        new(e.Id, e.MissionCode, e.Name, e.Description, e.Status, e.PlannedLaunchTimeUtc, e.ActualLaunchTimeUtc, e.IsSimulated, e.UpdatedAtUtc);

    /// <summary>Needs Vehicles (with Trajectories and Points) and Events loaded. Vehicles without a stored path are left out.</summary>
    public static Mission ToMission(MissionEntity e)
    {
        var typeById = e.Vehicles.ToDictionary(v => v.Id, v => v.Type);

        var events = e.Events
            .OrderBy(ev => ev.OffsetSeconds)
            .ThenBy(ev => ev.Name, StringComparer.Ordinal)
            .Select(ev => new MissionEvent(
                ev.OffsetSeconds,
                ev.Type,
                ev.Name,
                ev.Description,
                ev.Latitude,
                ev.Longitude,
                ev.AltitudeMeters,
                ev.VehicleId is { } vid ? typeById[vid] : null,
                ev.Quality,
                ev.SourceName))
            .ToList();

        var vehicles = e.Vehicles
            .OrderBy(v => v.Type)
            .Where(v => v.Trajectories.Count > 0)
            .Select(v => new Vehicle(
                v.Name,
                v.Type,
                v.ModelPath,
                v.Trajectories.OrderBy(t => t.Type).Select(ToFlightPath).ToList()))
            .ToList();

        var config = new MissionConfig(e.MissionCode, e.Name, e.PlannedLaunchTimeUtc, e.Description, events);
        return new Mission(config, new LaunchClock(e.PlannedLaunchTimeUtc), vehicles);
    }

    private static FlightPath ToFlightPath(TrajectoryEntity t) =>
        new(
            t.Type,
            t.Quality,
            t.SourceName,
            t.License,
            t.Points.OrderBy(p => p.OffsetSeconds)
                .Select(p => new TrajectoryPoint(
                    p.OffsetSeconds,
                    p.Latitude,
                    p.Longitude,
                    p.AltitudeMeters,
                    p.VelocityMps,
                    p.Quality,
                    p.SourceName,
                    p.HeadingDegrees,
                    p.PitchDegrees,
                    p.RollDegrees))
                .ToList());

    /// <summary>Builds the full entity graph of a mission for the first import. Throws for data that cannot be stored without loss.</summary>
    public static MissionEntity ToEntity(Mission mission, bool isSimulated, DateTimeOffset nowUtc, Guid plannedSourceId)
    {
        var entity = new MissionEntity
        {
            Id = Guid.CreateVersion7(),
            MissionCode = mission.MissionCode,
            Name = mission.Name,
            Description = mission.Config.Description,
            Status = MissionStatus.Scheduled,
            PlannedLaunchTimeUtc = mission.Clock.LaunchTimeUtc,
            ActualLaunchTimeUtc = null,
            IsSimulated = isSimulated,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        var idByType = new Dictionary<VehicleType, Guid>();
        foreach (var vehicle in mission.Vehicles)
        {
            var vehicleEntity = new VehicleEntity
            {
                Id = Guid.CreateVersion7(),
                MissionId = entity.Id,
                Name = vehicle.Name,
                Type = vehicle.Type,
                ModelPath = vehicle.ModelPath,
            };

            var seenTypes = new HashSet<TrajectoryType>();
            foreach (var path in vehicle.FlightPaths)
            {
                if (path.Type == TrajectoryType.Actual)
                {
                    throw new ArgumentException(
                        $"Mission '{mission.MissionCode}', vehicle {vehicle.Type}: Actual paths are built from telemetry and are not stored as trajectories.",
                        nameof(mission));
                }

                if (!seenTypes.Add(path.Type))
                {
                    throw new ArgumentException(
                        $"Mission '{mission.MissionCode}', vehicle {vehicle.Type}: more than one {path.Type} path.", nameof(mission));
                }

                vehicleEntity.Trajectories.Add(ToTrajectoryEntity(path, entity.Id, vehicleEntity.Id, plannedSourceId, nowUtc));
            }

            idByType[vehicle.Type] = vehicleEntity.Id;
            entity.Vehicles.Add(vehicleEntity);
        }

        foreach (var ev in mission.Config.Events)
        {
            Guid? vehicleId = null;
            if (ev.Vehicle is { } type)
            {
                vehicleId = idByType.TryGetValue(type, out var id)
                    ? id
                    : throw new MissionDataException(
                        $"Mission '{mission.MissionCode}': event '{ev.Name}' belongs to {type}, which has no trajectory in the mission.");
            }

            entity.Events.Add(new MissionEventEntity
            {
                Id = Guid.CreateVersion7(),
                MissionId = entity.Id,
                VehicleId = vehicleId,
                Type = ev.Type,
                OffsetSeconds = ev.OffsetSeconds,
                Name = ev.Name,
                Description = ev.Description,
                Latitude = ev.Latitude,
                Longitude = ev.Longitude,
                AltitudeMeters = ev.AltitudeMeters,
                Quality = ev.Quality,
                SourceName = ev.SourceName,
            });
        }

        return entity;
    }

    private static TrajectoryEntity ToTrajectoryEntity(FlightPath path, Guid missionId, Guid vehicleId, Guid sourceId, DateTimeOffset nowUtc)
    {
        var trajectory = new TrajectoryEntity
        {
            Id = Guid.CreateVersion7(),
            MissionId = missionId,
            VehicleId = vehicleId,
            Type = path.Type,
            Quality = path.Quality,
            SourceId = sourceId,
            SourceName = path.SourceName,
            License = path.License,
            CreatedAtUtc = nowUtc,
        };

        foreach (var p in path.Points)
        {
            trajectory.Points.Add(new TrajectoryPointEntity
            {
                OffsetSeconds = p.OffsetSeconds,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                AltitudeMeters = p.AltitudeMeters,
                VelocityMps = p.VelocityMetersPerSecond,
                HeadingDegrees = p.HeadingDegrees,
                PitchDegrees = p.PitchDegrees,
                RollDegrees = p.RollDegrees,
                Quality = p.Quality,
                SourceName = p.SourceName,
            });
        }

        return trajectory;
    }
}
