using System.IO.Compression;
using System.Text;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Kml.Packaging;

/// <summary>
/// Writes a deterministic KMZ: doc.kml first (UTF-8, no BOM), then the files sorted ordinally by path.
/// Entry timestamps, attributes and the compression level are fixed, so identical input gives identical bytes
/// (for the same runtime AND OS: the deflate stream comes from the runtime's zlib build, which can differ).
/// Entry paths are validated by <see cref="KmzEntry"/> (relative, forward slashes, no '..', 'doc.kml' reserved);
/// the packager only adds the case-insensitive duplicate check.
/// </summary>
public sealed class KmzPackager : IKmzPackager
{
    public const string DocEntryName = "doc.kml";

    private const CompressionLevel Level = CompressionLevel.Optimal;
    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Unix regular file, mode 0644, stored in the high 16 bits of the external attributes.
    private const int FixedExternalAttributes = unchecked((int)(0x81A4u << 16));

    public void Write(Stream output, string docKml, IReadOnlyList<KmzEntry> files)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(docKml);
        ArgumentNullException.ThrowIfNull(files);

        var sorted = new List<KmzEntry>(files.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (file is null)
            {
                throw new ArgumentException("The file list contains a null entry.", nameof(files));
            }

            if (!seen.Add(file.RelativePath))
            {
                throw new ArgumentException($"Duplicate KMZ entry path (case-insensitive): '{file.RelativePath}'.", nameof(files));
            }

            sorted.Add(file);
        }

        sorted.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Utf8NoBom);
        WriteEntry(archive, DocEntryName, Utf8NoBom.GetBytes(docKml));
        foreach (var file in sorted)
        {
            WriteEntry(archive, file.RelativePath, file.Content.Span);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, ReadOnlySpan<byte> content)
    {
        var entry = archive.CreateEntry(name, Level);
        entry.LastWriteTime = FixedTimestamp;
        entry.ExternalAttributes = FixedExternalAttributes;
        using var stream = entry.Open();
        stream.Write(content);
    }
}
