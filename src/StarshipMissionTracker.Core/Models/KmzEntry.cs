namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// A file inside the KMZ. The path is relative to the archive root, forward slashes, no '..' (e.g. files/starship.dae);
/// 'doc.kml' is reserved. The content is copied on construction, so the entry is immutable.
/// </summary>
public sealed record KmzEntry
{
    public KmzEntry(string relativePath, byte[] content)
        : this(relativePath, (ReadOnlyMemory<byte>)(content ?? throw new ArgumentNullException(nameof(content))))
    {
    }

    public KmzEntry(string relativePath, ReadOnlyMemory<byte> content)
    {
        Guard.RelativePath(relativePath, nameof(relativePath));
        if (string.Equals(relativePath, "doc.kml", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'doc.kml' is reserved for the main document.", nameof(relativePath));
        }

        RelativePath = relativePath;
        Content = content.ToArray();
    }

    public string RelativePath { get; }

    /// <summary>Private copy of the bytes.</summary>
    public ReadOnlyMemory<byte> Content { get; }
}
