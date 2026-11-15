using System.Xml;
using System.Xml.Schema;

namespace StarshipMissionTracker.Kml.Tests.Schema;

/// <summary>
/// Validates KML text against OGC KML 2.2 + the Google gx extension schema, fully offline: every schema
/// location (also the http(s) ones inside the XSDs) is mapped to the copies in <c>Schema/xsd</c>.
/// </summary>
internal static class KmlSchemaValidator
{
    public const string KmlNamespace = "http://www.opengis.net/kml/2.2";
    public const string GxNamespace = "http://www.google.com/kml/ext/2.2";

    private static readonly string[] LocalFiles = ["ogckml22.xsd", "atom-author-link.xsd", "xAL.xsd", "kml22gx.xsd"];

    public static string XsdDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Schema", "xsd");

    /// <summary>One schema problem with its position in the validated text.</summary>
    public sealed record Problem(XmlSeverityType Severity, int Line, int Column, string Message)
    {
        public override string ToString() => $"{Severity} @{Line}:{Column} {Message}";
    }

    public static IReadOnlyList<Problem> Validate(string kml, bool includeGx = true)
    {
        var set = CreateSchemaSet(includeGx);
        var problems = new List<Problem>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = set,
            XmlResolver = null,
            DtdProcessing = DtdProcessing.Prohibit,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
        };
        settings.ValidationEventHandler += (_, e) =>
            problems.Add(new Problem(e.Severity, e.Exception?.LineNumber ?? 0, e.Exception?.LinePosition ?? 0, e.Message));

        using var text = new StringReader(kml);
        using var reader = XmlReader.Create(text, settings);
        while (reader.Read())
        {
        }

        return problems;
    }

    public static XmlSchemaSet CreateSchemaSet(bool includeGx)
    {
        var set = new XmlSchemaSet { XmlResolver = new LocalResolver() };
        var errors = new List<string>();
        set.ValidationEventHandler += (_, e) => errors.Add(e.Message);
        set.Add(KmlNamespace, Path.Combine(XsdDirectory, "ogckml22.xsd"));
        if (includeGx)
        {
            set.Add(GxNamespace, Path.Combine(XsdDirectory, "kml22gx.xsd"));
        }

        set.Compile();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("The bundled schemas do not compile: " + string.Join("; ", errors));
        }

        return set;
    }

    /// <summary>Maps any schema location (http, https or file) by file name to the local copy; never touches the network.</summary>
    private sealed class LocalResolver : XmlUrlResolver
    {
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            var name = Path.GetFileName((relativeUri ?? string.Empty).Replace('\\', '/'));
            var local = LocalFiles.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            if (local is null)
            {
                throw new InvalidOperationException($"Unexpected schema location '{relativeUri}' (not bundled; tests must stay offline).");
            }

            return new Uri(Path.Combine(XsdDirectory, local));
        }
    }
}
