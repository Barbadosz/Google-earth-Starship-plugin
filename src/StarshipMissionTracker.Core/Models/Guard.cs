using System.Collections.Immutable;

namespace StarshipMissionTracker.Core.Models;

/// <summary>Shared argument validation for the model constructors.</summary>
internal static class Guard
{
    public static double Finite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be a finite number.");
        }

        return value;
    }

    public static double Latitude(double value, string paramName)
    {
        if (!double.IsFinite(value) || value is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Latitude must be within [-90, 90].");
        }

        return value;
    }

    /// <summary>
    /// Antimeridian rule: longitude is stored in [-180, 180). Input in [-180, 180] is accepted and +180 is
    /// normalised to -180 (same meridian); anything outside is rejected.
    /// </summary>
    public static double Longitude(double value, string paramName)
    {
        if (!double.IsFinite(value) || value is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Longitude must be within [-180, 180].");
        }

        return value == 180 ? -180 : value;
    }

    /// <summary>Copies the items into an immutable array; null list or null element throws.</summary>
    public static ImmutableArray<T> CopyNoNulls<T>(IEnumerable<T> items, string paramName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items, paramName);
        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("The list must not contain null elements.", paramName);
            }

            builder.Add(item);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Relative archive path: not blank, not rooted, forward slashes only, no empty/'.'/'..' segments,
    /// no drive colon or control characters (zip-slip protection).
    /// </summary>
    public static void RelativePath(string path, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);

        if (path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl))
        {
            throw new ArgumentException($"'{path}' is not a valid relative path (backslash, colon or control character).", paramName);
        }

        if (path.StartsWith('/') || Path.IsPathRooted(path))
        {
            throw new ArgumentException($"'{path}' must be a relative path.", paramName);
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new ArgumentException($"'{path}' contains an empty, '.' or '..' segment.", paramName);
            }
        }
    }
}
