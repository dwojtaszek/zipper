using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Applies structural EDRM-XML chaos anomalies to a rendered XML document.
/// <para>
/// <see cref="XmlLoadFileWriter"/> is the documented EDRM-XML carve-out from the
/// composer → serializer → emitter seam (ADR-0006, ADR-0007 and docs/architecture.md).
/// It builds a hierarchical XML document tree, so the line-oriented <see cref="Apply"/>
/// contract is a no-op; XML anomalies are applied by <see cref="XmlLoadFileWriter"/> to the
/// rendered document string inside the carve-out boundary.
/// </para>
/// </summary>
internal sealed class XmlChaosAnomalyApplier : IAnomalyApplier
{
    private const string ClosingDocumentTag = "</Document>";

    /// <inheritdoc/>
    /// <remarks>Always returns <paramref name="line"/> unchanged; XML is not a delimited line format.</remarks>
    public string Apply(
        long lineNumber,
        string line,
        string recordId,
        string chaosType,
        List<ChaosAnomaly> anomalies,
        LoadFileRecord? record = null,
        ILoadFileSerializer? serializer = null)
        => line;

    /// <inheritdoc/>
    /// <remarks>Always returns <c>null</c>; XML anomaly injection happens per document.</remarks>
    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies)
        => null;

    /// <summary>
    /// Applies a document-scoped anomaly. Returns <c>false</c> when the type does not apply to
    /// this document and the output is unchanged.
    /// </summary>
    public bool TryApplyToDocument(string documentXml, string chaosType, out string result, out string column, out string description)
    {
        switch (chaosType)
        {
            case "xml-unclosed-tag":
                int closingTag = documentXml.LastIndexOf(ClosingDocumentTag, StringComparison.Ordinal);
                if (closingTag < 0)
                {
                    result = documentXml;
                    column = "N/A";
                    description = string.Empty;
                    return false;
                }

                result = documentXml.Remove(closingTag, ClosingDocumentTag.Length);
                column = "Document";
                description = "Removed the closing </Document> tag, leaving the EDRM-XML document element unclosed.";
                return true;

            case "xml-bad-entity":
                int insertionPoint = documentXml.IndexOf("<Tags>", StringComparison.Ordinal);
                if (insertionPoint < 0)
                {
                    insertionPoint = documentXml.LastIndexOf(ClosingDocumentTag, StringComparison.Ordinal);
                }

                if (insertionPoint < 0)
                {
                    result = documentXml;
                    column = "N/A";
                    description = string.Empty;
                    return false;
                }

                result = documentXml.Insert(insertionPoint, "&chaos;");
                column = "Document";
                description = "Injected an undefined XML entity reference (&chaos;) that conforming parsers reject.";
                return true;

            default:
                result = documentXml;
                column = "N/A";
                description = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// Rewrites the XML declaration so it claims an encoding that differs from the emitted bytes.
    /// Returns <c>false</c> when no declaration encoding is present.
    /// </summary>
    public static bool TryApplyEncodingLie(string xml, out string result)
    {
        const string xmlDeclarationPrefix = "<?xml";
        const string utf8Declaration = "encoding=\"utf-8\"";
        const string utf16Declaration = "encoding=\"utf-16\"";
        int declarationStart = xml.StartsWith('\uFEFF') ? 1 : 0;

        if (!xml.AsSpan(declarationStart).StartsWith(xmlDeclarationPrefix, StringComparison.Ordinal)
            || xml.Length <= declarationStart + xmlDeclarationPrefix.Length
            || xml[declarationStart + xmlDeclarationPrefix.Length] is not (' ' or '\t' or '\r' or '\n'))
        {
            result = xml;
            return false;
        }

        int declarationEnd = xml.IndexOf("?>", declarationStart, StringComparison.Ordinal);
        int encodingIndex = declarationEnd < 0
            ? -1
            : xml.IndexOf(utf8Declaration, declarationStart, declarationEnd - declarationStart, StringComparison.Ordinal);

        if (encodingIndex < 0)
        {
            result = xml;
            return false;
        }

        result = string.Concat(
            xml.AsSpan(0, encodingIndex),
            utf16Declaration,
            xml.AsSpan(encodingIndex + utf8Declaration.Length));
        return true;
    }
}
