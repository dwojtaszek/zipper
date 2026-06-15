using System.Text;

namespace Zipper;

internal interface IAnomalyApplier
{
    string Apply(long lineNumber, string line, string recordId, string chaosType, List<ChaosAnomaly> anomalies);
    byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies);
}
