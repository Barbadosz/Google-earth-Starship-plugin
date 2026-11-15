using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Trajectory.Loading;

/// <summary>
/// Loads a mission from <c>data/missions/&lt;code&gt;.json</c> plus <c>&lt;code&gt;-&lt;vehicle&gt;-planned.json</c> trajectory files.
/// Every invalid input is reported as <see cref="MissionDataException"/> naming the file, field (with list index) and value.
/// Unknown JSON fields are rejected. A launch-time override wins over the file value; the file value is then not even
/// parsed (so an invalid <c>launchTimeUtc</c> in the file does not block a valid override).
/// </summary>
public sealed partial class MissionFileLoader : IMissionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,

        // A typo in a field name must fail loudly instead of silently dropping data.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

        // A repeated key would silently keep the last value; reject it (the JsonException names the key).
        AllowDuplicateProperties = false,
    };

    private static readonly string[] EventFields = ["offsetSeconds:t", "latitude:lat", "longitude:lon", "altitudeMeters:altM"];

    private static readonly string[] PointFields =
    [
        "offsetSeconds:t", "latitude:lat", "longitude:lon", "altitudeMeters:altM",
        "velocityMetersPerSecond:velocityMps", "headingDegrees:headingDeg", "pitchDegrees:pitchDeg", "rollDegrees:rollDeg",
    ];

    public async Task<Mission> LoadAsync(
        string missionConfigPath,
        string trajectoriesDirectory,
        DateTimeOffset? launchTimeOverrideUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(trajectoriesDirectory);
        var missionFile = Path.GetFileName(missionConfigPath);

        if (launchTimeOverrideUtc is { } o && o.Offset != TimeSpan.Zero)
        {
            throw new MissionDataException(
                $"Launch time override '{o:O}' is not UTC; pass a UTC time like 2026-12-01T23:30:00Z.");
        }

        var dto = await ReadAsync<MissionFileDto>(missionConfigPath, cancellationToken).ConfigureAwait(false);
        try
        {
            var config = BuildConfig(dto, missionFile, launchTimeOverrideUtc is not null);

            // The file name is the requested mission code (<data>/missions/<code>.json); ordinal exact match, like the code pattern.
            var requestedCode = Path.GetFileNameWithoutExtension(missionConfigPath);
            if (!string.Equals(config.MissionCode, requestedCode, StringComparison.Ordinal))
            {
                throw new MissionDataException(
                    $"{missionFile}: field 'missionCode' value '{config.MissionCode}' does not match the requested mission code '{requestedCode}'.");
            }

            var vehicles = new List<Vehicle>();
            foreach (var v in dto.Vehicles ?? [])
            {
                var type = ParseEnum<VehicleType>(v.Type, missionFile, "vehicles[].type");
                var path = Path.Combine(trajectoriesDirectory, $"{config.MissionCode}-{VehicleFileToken(type)}-planned.json");
                var flightPath = await LoadFlightPathAsync(path, type, cancellationToken).ConfigureAwait(false);
                vehicles.Add(new Vehicle(RequireText(v.Name, missionFile, "vehicles[].name"), type, v.ModelPath, [flightPath]));
            }

            if (vehicles.Count == 0)
            {
                throw new MissionDataException($"{missionFile}: field 'vehicles' must list at least one vehicle.");
            }

            if (launchTimeOverrideUtc is null && config.LaunchTimeUtc is null)
            {
                throw new MissionDataException(
                    $"No launch time (T0): set launchTimeUtc in {missionFile} or pass --launch-time.");
            }

            return Mission.Create(config, launchTimeOverrideUtc, vehicles);
        }
        catch (ArgumentException ex)
        {
            throw new MissionDataException($"{missionFile}: invalid mission data: {ex.Message}", ex);
        }
    }

    /// <summary>Mission codes become part of file names, so only a safe character set is accepted (no path traversal).</summary>
    /// <remarks>\z (not $) so a trailing newline fails; Windows reserved device names (CON, NUL, COM1, ...) are rejected.</remarks>
    [GeneratedRegex(@"^(?!(?i:con|prn|aux|nul|com[1-9]|lpt[1-9])\z)[A-Za-z0-9][A-Za-z0-9_-]*\z")]
    private static partial Regex MissionCodePattern();

    private static MissionConfig BuildConfig(MissionFileDto dto, string file, bool hasOverride)
    {
        var code = RequireText(dto.MissionCode, file, "missionCode");
        if (!MissionCodePattern().IsMatch(code))
        {
            throw new MissionDataException(
                $"{file}: field 'missionCode' value '{code}' is invalid (allowed: letters, digits, '_' and '-', starting with a letter or digit).");
        }

        DateTimeOffset? t0 = null;
        if (dto.LaunchTimeUtc is not null && !hasOverride)
        {
            if (!LaunchClock.TryParseUtc(dto.LaunchTimeUtc, out var parsed))
            {
                throw new MissionDataException(
                    $"{file}: field 'launchTimeUtc' value '{dto.LaunchTimeUtc}' must be UTC ISO 8601 like 2026-12-01T23:30:00Z.");
            }

            t0 = parsed;
        }

        var events = new List<MissionEvent>();
        var eventList = dto.Events ?? [];
        for (var i = 0; i < eventList.Count; i++)
        {
            var e = eventList[i];
            var at = $"events[{i}]";
            var type = ParseEnum<EventType>(e.Type, file, $"{at}.type");
            var vehicle = e.Vehicle is null ? (VehicleType?)null : ParseEnum<VehicleType>(e.Vehicle, file, $"{at}.vehicle");
            var quality = ParseEnum<DataQuality>(e.Quality ?? dto.Quality, file, $"{at}.quality");
            var t = RequireNumber(e.T, file, $"{at}.t");
            var name = RequireText(e.Name, file, $"{at}.name");
            var source = RequireText(e.Source ?? dto.Source, file, $"{at}.source");
            try
            {
                events.Add(new MissionEvent(
                    t, type, name, e.Description ?? string.Empty, e.Lat, e.Lon, e.AltM, vehicle, quality, source));
            }
            catch (ArgumentException ex)
            {
                throw new MissionDataException(
                    $"{file}: {at}{FieldHint(ex, EventFields, t, e.Lat, e.Lon, e.AltM)}: {ex.Message}", ex);
            }
        }

        return new MissionConfig(
            code,
            RequireText(dto.Name, file, "name"),
            t0,
            dto.Description ?? string.Empty,
            events);
    }

    private static async Task<FlightPath> LoadFlightPathAsync(string path, VehicleType expectedVehicle, CancellationToken ct)
    {
        var file = Path.GetFileName(path);
        var dto = await ReadAsync<TrajectoryFileDto>(path, ct).ConfigureAwait(false);

        // Optional field; when present it must agree with the vehicle the file name was derived from.
        if (dto.Vehicle is not null && ParseEnum<VehicleType>(dto.Vehicle, file, "vehicle") != expectedVehicle)
        {
            throw new MissionDataException(
                $"{file}: field 'vehicle' value '{dto.Vehicle}' does not match the file name (expected {expectedVehicle}).");
        }

        var type = dto.Type is null ? TrajectoryType.Planned : ParseEnum<TrajectoryType>(dto.Type, file, "type");
        var quality = ParseEnum<DataQuality>(dto.Quality, file, "quality");
        var source = RequireText(dto.Source, file, "source");
        var points = new List<TrajectoryPoint>();
        var pointList = dto.Points ?? [];
        for (var i = 0; i < pointList.Count; i++)
        {
            var p = pointList[i];
            var at = $"points[{i}]";
            var pq = p.Quality is null ? quality : ParseEnum<DataQuality>(p.Quality, file, $"{at}.quality");
            var t = RequireNumber(p.T, file, $"{at}.t");
            var lat = RequireNumber(p.Lat, file, $"{at}.lat");
            var lon = RequireNumber(p.Lon, file, $"{at}.lon");
            var alt = RequireNumber(p.AltM, file, $"{at}.altM");
            try
            {
                points.Add(new TrajectoryPoint(
                    t, lat, lon, alt, p.VelocityMps, pq, p.Source ?? source, p.HeadingDeg, p.PitchDeg, p.RollDeg));
            }
            catch (ArgumentException ex)
            {
                var hint = FieldHint(ex, PointFields, t, lat, lon, alt, p.VelocityMps, p.HeadingDeg, p.PitchDeg, p.RollDeg);
                throw new MissionDataException($"{file}: {at}{hint}: {ex.Message}", ex);
            }
        }

        if (points.Count == 0)
        {
            throw new MissionDataException($"{file}: field 'points' must contain at least one point.");
        }

        try
        {
            return new FlightPath(type, quality, source, dto.License, points);
        }
        catch (ArgumentException ex)
        {
            throw new MissionDataException($"{file}: invalid trajectory: {ex.Message}", ex);
        }
    }

    /// <summary>Maps the constructor parameter named by the exception to ".jsonField = value" (empty if unknown).</summary>
    private static string FieldHint(ArgumentException ex, string[] fields, params double?[] values)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            var split = fields[i].Split(':');
            if (split[0] == ex.ParamName)
            {
                return $".{split[1]} = {values[i]?.ToString(CultureInfo.InvariantCulture)}";
            }
        }

        return string.Empty;
    }

    private static async Task<T> ReadAsync<T>(string path, CancellationToken ct)
        where T : class
    {
        var file = Path.GetFileName(path);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false)
                ?? throw new MissionDataException($"{file}: file is empty or 'null'.");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new MissionDataException($"{file}: file not found ({path}).", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MissionDataException($"{file}: cannot read file: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new MissionDataException($"{file}: invalid JSON (line {ex.LineNumber}): {ex.Message}", ex);
        }
    }

    private static TEnum ParseEnum<TEnum>(string? text, string file, string field)
        where TEnum : struct, Enum
    {
        if (text is null)
        {
            throw new MissionDataException($"{file}: required field '{field}' is missing.");
        }

        // Exact (case-insensitive) name match only: Enum.TryParse would also accept numbers, padded text and "A,B" lists.
        var name = Enum.GetNames<TEnum>().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            throw new MissionDataException(
                $"{file}: field '{field}' has unknown value '{text}' (allowed: {string.Join(", ", Enum.GetNames<TEnum>())}).");
        }

        return Enum.Parse<TEnum>(name);
    }

    private static string RequireText(string? text, string file, string field) =>
        string.IsNullOrWhiteSpace(text)
            ? throw new MissionDataException($"{file}: required field '{field}' is missing or empty.")
            : text;

    private static double RequireNumber(double? value, string file, string field) =>
        value ?? throw new MissionDataException($"{file}: required numeric field '{field}' is missing.");

    private static string VehicleFileToken(VehicleType type) => type switch
    {
        VehicleType.Starship => "starship",
        VehicleType.SuperHeavy => "superheavy",
        _ => throw new MissionDataException($"No trajectory file naming defined for vehicle type '{type}'."),
    };
}
