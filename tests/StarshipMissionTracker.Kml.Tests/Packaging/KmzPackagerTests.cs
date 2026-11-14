using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Packaging;

namespace StarshipMissionTracker.Kml.Tests.Packaging;

public class KmzPackagerTests
{
    private const string Doc = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>á</Document></kml>";

    private static KmzEntry Entry(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    private static List<KmzEntry> SampleFiles() =>
    [
        Entry("files/starship.dae", "starship-model"),
        Entry("files/booster.dae", "booster-model"),
        Entry("Files/icons/a.png", "icon"),
    ];

    private static byte[] Pack(string doc, IReadOnlyList<KmzEntry> files)
    {
        using var ms = new MemoryStream();
        new KmzPackager().Write(ms, doc, files);
        return ms.ToArray();
    }

    [Fact]
    public void Write_DocKmlIsFirstEntry_ThenFilesSortedOrdinally()
    {
        using var zip = new ZipArchive(new MemoryStream(Pack(Doc, SampleFiles())), ZipArchiveMode.Read);

        Assert.Equal(
            ["doc.kml", "Files/icons/a.png", "files/booster.dae", "files/starship.dae"],
            zip.Entries.Select(e => e.FullName).ToArray());
    }

    [Fact]
    public void Write_EntriesReadableBack_ContentMatches_DocHasNoBom()
    {
        var bytes = Pack(Doc, SampleFiles());
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.Equal("starship-model", ReadText(zip.GetEntry("files/starship.dae")!));
        Assert.Equal("booster-model", ReadText(zip.GetEntry("files/booster.dae")!));

        var docBytes = ReadBytes(zip.GetEntry("doc.kml")!);
        Assert.False(docBytes.Length >= 3 && docBytes[0] == 0xEF && docBytes[1] == 0xBB && docBytes[2] == 0xBF);
        Assert.Equal(Doc, Encoding.UTF8.GetString(docBytes));
    }

    [Fact]
    public void Write_SameInputTwice_ByteIdentical()
    {
        Assert.Equal(Pack(Doc, SampleFiles()), Pack(Doc, SampleFiles()));
    }

    [Fact]
    public void Write_InputOrderDoesNotMatter()
    {
        var reversed = SampleFiles();
        reversed.Reverse();
        Assert.Equal(Pack(Doc, SampleFiles()), Pack(Doc, reversed));
    }

    [Fact]
    public void Write_DuplicatePathCaseInsensitive_Throws()
    {
        var files = new[] { Entry("files/a.dae", "1"), Entry("Files/A.DAE", "2") };
        Assert.Throws<ArgumentException>(() => Pack(Doc, files));
    }

    // The packager relies on KmzEntry (Core) validating paths: these can never become archive entries.
    [Theory]
    [InlineData("../x")]
    [InlineData("files/../../x")]
    [InlineData("/x")]
    [InlineData("files\\x")]
    [InlineData("C:/x")]
    [InlineData("doc.kml")]
    [InlineData("DOC.KML")]
    [InlineData("./files/x")]
    [InlineData("files//x")]
    [InlineData("")]
    public void KmzEntry_DangerousPath_RejectedBeforeReachingPackager(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => Entry(path, "x"));
    }

    [Fact]
    public void Write_EmptyFileList_OnlyDocKml()
    {
        using var zip = new ZipArchive(new MemoryStream(Pack(Doc, [])), ZipArchiveMode.Read);
        Assert.Equal(["doc.kml"], zip.Entries.Select(e => e.FullName).ToArray());
    }

    [Fact]
    public void Write_NullArguments_Throw()
    {
        var p = new KmzPackager();
        Assert.Throws<ArgumentNullException>(() => p.Write(null!, Doc, []));
        Assert.Throws<ArgumentNullException>(() => p.Write(new MemoryStream(), null!, []));
        Assert.Throws<ArgumentNullException>(() => p.Write(new MemoryStream(), Doc, null!));
    }

    [Fact]
    public void Write_LeavesOutputStreamOpen()
    {
        using var ms = new MemoryStream();
        new KmzPackager().Write(ms, Doc, []);
        Assert.True(ms.CanWrite);
    }

    [Fact]
    public void Write_FixedTimestamps()
    {
        using var zip = new ZipArchive(new MemoryStream(Pack(Doc, SampleFiles())), ZipArchiveMode.Read);
        Assert.All(zip.Entries, e => Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 0), e.LastWriteTime.DateTime));
    }

    // Golden: structure and content of the archive. The raw compressed bytes are deliberately not pinned,
    // because the deflate output depends on the runtime's zlib build.
    [Fact]
    public void Write_FixedInput_MatchesGoldenManifest()
    {
        var files = new List<KmzEntry> { Entry("files/b.txt", "bbb"), Entry("files/a.txt", "aaa") };
        var bytes = Pack("<kml/>", files);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var manifest = string.Join("\n", zip.Entries.Select(e =>
            $"{e.FullName}|{e.Length}|{e.LastWriteTime:yyyy-MM-dd HH:mm:ss}|{Hash(ReadBytes(e))}"));

        var expected = string.Join("\n",
            "doc.kml|6|1980-01-01 00:00:00|" + Hash(Encoding.UTF8.GetBytes("<kml/>")),
            "files/a.txt|3|1980-01-01 00:00:00|" + Hash(Encoding.UTF8.GetBytes("aaa")),
            "files/b.txt|3|1980-01-01 00:00:00|" + Hash(Encoding.UTF8.GetBytes("bbb")));
        Assert.Equal(expected, manifest);
        Assert.Equal("504B0304", Convert.ToHexString(bytes.AsSpan(0, 4)));
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..16];

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ReadText(ZipArchiveEntry entry) => Encoding.UTF8.GetString(ReadBytes(entry));
}
