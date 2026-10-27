using System.Text;

namespace Zipper;

/// <summary>
/// Chaos Engine: deliberately injects structural and encoding anomalies into load file lines.
/// Acts as a line-level interceptor before output is written to the stream.
/// </summary>
/// <remarks>
/// This class is not thread-safe. Instances should not be shared across threads.
/// </remarks>
internal class ChaosEngine
{
    private static readonly string[] DatChaosTypes = ChaosAnomalyTypes.Dat.ToArray();
    private static readonly string[] OptChaosTypes = ChaosAnomalyTypes.Opt.ToArray();
    private static readonly string[] OptDefaultChaosTypes = OptChaosTypes.Where(type => type != "opt-encoding").ToArray();

    private readonly HashSet<string> enabledTypes;
    private readonly List<ChaosAnomaly> anomalies = new();
    private int anomalyTypeIndex;

    private readonly IAnomalyApplier applier;
    private readonly LoadFileFormat format;
    private readonly long totalLines;
    private readonly bool onlyOptEncoding;
    private readonly ChaosSampler sampler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChaosEngine"/> class.
    /// </summary>
    /// <param name="totalLines">Total number of lines (including header).</param>
    /// <param name="chaosAmount">Amount string: percentage (e.g., "1%") or exact count (e.g., "500").</param>
    /// <param name="chaosTypes">Comma-separated type filter, or null for all.</param>
    /// <param name="format">Load file format (Dat or Opt).</param>
    /// <param name="columnDelimiter">Configured column delimiter.</param>
    /// <param name="quoteDelimiter">Configured quote delimiter.</param>
    /// <param name="eol">Configured end-of-line string.</param>
    /// <param name="seed">Optional random seed for reproducibility.</param>
    public ChaosEngine(
        long totalLines,
        string? chaosAmount,
        string? chaosTypes,
        LoadFileFormat format,
        string columnDelimiter,
        string quoteDelimiter,
        string eol,
        int? seed = null)
    {
        this.format = format;
        this.totalLines = totalLines;

#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        Random random = seed.HasValue ? new Random(seed.Value) : new Random();
#pragma warning restore S2245

        // Determine enabled types
        var validTypes = format == LoadFileFormat.Opt ? OptChaosTypes : DatChaosTypes;
        if (!string.IsNullOrEmpty(chaosTypes))
        {
            this.enabledTypes = new HashSet<string>(
                chaosTypes.Split(',').Select(t => t.Trim().ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
            this.enabledTypes.IntersectWith(validTypes);
        }
        else
        {
            var defaultTypes = format == LoadFileFormat.Opt ? OptDefaultChaosTypes : validTypes;
            this.enabledTypes = new HashSet<string>(defaultTypes, StringComparer.OrdinalIgnoreCase);
        }

        this.onlyOptEncoding = format == LoadFileFormat.Opt
            && this.enabledTypes.Count == 1
            && this.enabledTypes.Contains("opt-encoding");
        ChaosSampler.ValidateTotalLines(totalLines);
        long targetableLines = this.onlyOptEncoding && totalLines > 1 ? totalLines - 1 : totalLines;
        this.sampler = new ChaosSampler(targetableLines, chaosAmount, random);

        // If quote-delim is "none", disable the quotes chaos type
        if (string.IsNullOrEmpty(quoteDelimiter))
        {
            this.enabledTypes.Remove("quotes");
        }

        // Initialize Applier
        this.applier = format == LoadFileFormat.Opt
            ? new OptAnomalyApplier(random)
            : new DatAnomalyApplier(random, columnDelimiter, quoteDelimiter, string.IsNullOrEmpty(eol) ? "\r\n" : eol);
    }

    /// <summary>
    /// Gets the list of injected anomalies for audit output.
    /// </summary>
    public IReadOnlyList<ChaosAnomaly> Anomalies => this.anomalies;

    /// <summary>
    /// Determines if a line should be intercepted by the chaos engine.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <returns>True if the line should be corrupted.</returns>
    public bool ShouldIntercept(long lineNumber) =>
        !(this.onlyOptEncoding && lineNumber >= this.totalLines) && this.sampler.ShouldIntercept(lineNumber);

    /// <summary>
    /// Intercepts and corrupts a line. Returns the modified line.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="line">Original line content.</param>
    /// <param name="recordId">Record ID (e.g., "DOC00001054" or "HEADER").</param>
    /// <returns>Modified line with injected anomaly.</returns>
    public string Intercept(long lineNumber, string line, string recordId)
    {
        if (this.enabledTypes.Count == 0)
        {
            return line;
        }

        var typeList = this.enabledTypes.ToArray();
        var chosenType = typeList[this.anomalyTypeIndex % typeList.Length];
        this.anomalyTypeIndex++;

        if (this.format == LoadFileFormat.Opt && lineNumber == this.totalLines && chosenType == "opt-encoding")
        {
            if (this.onlyOptEncoding)
            {
                return line;
            }

            chosenType = typeList.First(type => type != "opt-encoding");
        }

        return this.applier.Apply(lineNumber, line, recordId, chosenType, this.anomalies);
    }

    /// <summary>
    /// Generates encoding-anomaly bytes to inject between two lines.
    /// </summary>
    /// <param name="lineNumber">Line number after which to inject.</param>
    /// <param name="nextLineNumber">Next line number.</param>
    /// <param name="encoding">Target encoding.</param>
    /// <returns>Injected byte array, or null if encoding chaos is not enabled or not targeted.</returns>
    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding)
    {
        return this.applier.GetEncodingAnomaly(lineNumber, nextLineNumber, encoding, this.anomalies);
    }
}
