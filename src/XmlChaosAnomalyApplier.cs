using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// No-op applier for the EDRM-XML carve-out.
/// <para>
/// <see cref="XmlLoadFileWriter"/> is the documented EDRM-XML carve-out from the
/// composer → serializer → emitter seam (ADR-0006, ADR-0007 and docs/architecture.md).
/// It builds a hierarchical XML document tree and writes it via <see cref="System.Xml.XmlWriter"/>,
/// bypassing <see cref="LoadFileEmitter"/> entirely. XML anomalies are therefore injected
/// inside <see cref="XmlLoadFileWriter.WriteAsync"/> rather than through this applier.
/// </para>
/// <para>
/// This class satisfies the <see cref="IAnomalyApplier"/> contract required by
/// <see cref="ChaosEngine"/>, but its <see cref="Apply"/> and <see cref="GetEncodingAnomaly"/>
/// methods always return the original line / null — they are never called in practice because
/// <see cref="ChaosEngine.Intercept"/> is not invoked by <see cref="XmlLoadFileWriter"/>.
/// </para>
/// </summary>
internal sealed class XmlChaosAnomalyApplier : IAnomalyApplier
{
    // random retained for future use should any XML line-interception path be added
    // under the carve-out boundary.
#pragma warning disable IDE0052
    private readonly Random random;
#pragma warning restore IDE0052

    public XmlChaosAnomalyApplier(Random random)
    {
        this.random = random;
    }

    /// <inheritdoc/>
    /// <remarks>Always returns <paramref name="line"/> unchanged. XML anomaly injection is
    /// handled inside <see cref="XmlLoadFileWriter.WriteAsync"/> within the carve-out.</remarks>
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
    /// <remarks>Always returns <c>null</c>; XML output is not a line-delimited byte stream.</remarks>
    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies)
        => null;
}
