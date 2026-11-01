using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Chaos Engine: deliberately injects structural and encoding anomalies into load file lines.
/// Acts as a line-level interceptor before output is written to the stream.
/// </summary>
/// <remarks>
/// Line-level chaos is not thread-safe. Instances used for line-level chaos should not be shared across threads.
/// Email chaos (<see cref="ApplyEmailChaos(FileWorkItem, GeneratedFileContent)"/>) is safe for concurrent calls
/// as long as each work item is applied once.
/// </remarks>
internal class ChaosEngine
{
    private static readonly string[] DatChaosTypes = ChaosAnomalyTypes.Dat.ToArray();
    private static readonly string[] DatDefaultChaosTypes = ChaosAnomalyTypes.DatDefault.ToArray();
    private static readonly HashSet<string> DatRecordOnlyChaosTypes = new(ChaosAnomalyTypes.DatRecordOnly, StringComparer.Ordinal);
    private static readonly string[] OptChaosTypes = ChaosAnomalyTypes.Opt.ToArray();
    private static readonly string[] OptDefaultChaosTypes = OptChaosTypes.Where(type => type != "opt-encoding").ToArray();
    private static readonly string[] CsvChaosTypes = ChaosAnomalyTypes.Csv.ToArray();
    private static readonly string[] ConcordanceChaosTypes = ChaosAnomalyTypes.Concordance.ToArray();
    private static readonly string[] EdrmXmlChaosTypes = ChaosAnomalyTypes.EdrmXml.ToArray();
    private static readonly string[] EmailChaosTypes = ChaosAnomalyTypes.Email.ToArray();

    private readonly HashSet<string> enabledTypes;
    private readonly List<ChaosAnomaly> anomalies = new();
    private int anomalyTypeIndex;

    private readonly IAnomalyApplier? applier;
    private readonly LoadFileFormat format;
    private readonly long totalLines;
    private readonly bool onlyOptEncoding;
    private readonly bool onlyCsvBom;
    private readonly bool hasDatRecordOnlyTypes;
    private readonly bool noDatRecords;
    private readonly ChaosSampler sampler;

    // Email chaos: the sampled work items, and one audit slot per sampled item in work item order.
    private readonly int emailSeed;
    private readonly BatesSequence? emailBatesSequence;
    private readonly Dictionary<long, EmailSample>? emailSamples;
    private readonly ChaosAnomaly?[]? emailAnomalies;

    private readonly record struct EmailSample(int Slot, string Type);

    /// <summary>
    /// Initializes a new instance of the <see cref="ChaosEngine"/> class.
    /// </summary>
    /// <param name="totalLines">Total number of lines (including header).</param>
    /// <param name="chaosAmount">Amount string: percentage (e.g., "1%") or exact count (e.g., "500").</param>
    /// <param name="chaosTypes">Comma-separated type filter, null for defaults, or empty string for all types.</param>
    /// <param name="format">Load file format (Dat or Opt).</param>
    /// <param name="columnDelimiter">Configured column delimiter.</param>
    /// <param name="quoteDelimiter">Configured quote delimiter.</param>
    /// <param name="eol">Configured end-of-line string.</param>
    /// <param name="seed">Optional random seed for reproducibility.</param>
    /// <param name="dateColumnNames">Optional names of date columns that the <c>date-format</c> anomaly may target.</param>
    /// <param name="sourceFilePaths">Optional Source Record paths used to avoid path-pointer collisions.</param>
    /// <param name="dateColumnIndexes">Optional date-column indexes resolved from a Column Profile.</param>
    public ChaosEngine(
        long totalLines,
        string? chaosAmount,
        string? chaosTypes,
        LoadFileFormat format,
        string columnDelimiter,
        string quoteDelimiter,
        string eol,
        int? seed = null,
        IReadOnlyCollection<string>? dateColumnNames = null,
        IReadOnlySet<string>? sourceFilePaths = null,
        IReadOnlySet<int>? dateColumnIndexes = null)
    {
        this.format = format;
        this.totalLines = totalLines;

#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        Random random = seed.HasValue ? new Random(seed.Value) : new Random();
#pragma warning restore S2245

        this.enabledTypes = ResolveEnabledTypes(format, chaosTypes);

        this.onlyOptEncoding = format == LoadFileFormat.Opt
            && this.enabledTypes.Count == 1
            && this.enabledTypes.Contains("opt-encoding");
        this.onlyCsvBom = format == LoadFileFormat.Csv
            && this.enabledTypes.Count == 1
            && this.enabledTypes.Contains("csv-bom");
        this.hasDatRecordOnlyTypes = format == LoadFileFormat.Dat
            && this.enabledTypes.Count > 0
            && this.enabledTypes.Any(DatRecordOnlyChaosTypes.Contains);
        this.noDatRecords = this.hasDatRecordOnlyTypes && totalLines == 1;
        ChaosSampler.ValidateTotalLines(totalLines);
        long targetableLines = GetTargetableLines(totalLines, this.onlyOptEncoding, this.onlyCsvBom, this.hasDatRecordOnlyTypes);
        this.sampler = new ChaosSampler(targetableLines, chaosAmount, random);

        // If quote-delim is "none", disable the quotes chaos type
        if (string.IsNullOrEmpty(quoteDelimiter))
        {
            this.enabledTypes.Remove("quotes");
        }

        // Initialize Applier
        this.applier = CreateApplier(
            format,
            random,
            columnDelimiter,
            quoteDelimiter,
            eol,
            dateColumnNames,
            sourceFilePaths,
            dateColumnIndexes);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChaosEngine"/> class for email work items.
    /// </summary>
    /// <param name="totalItems">Number of eligible work items to sample over.</param>
    /// <param name="chaosAmount">Amount string: percentage (e.g., "1%") or exact count (e.g., "500").</param>
    /// <param name="chaosTypes">Comma-separated email type filter, or null or empty for all email types.</param>
    /// <param name="seed">Optional run seed. Each work item derives its own random sequence from this seed and its index.</param>
    /// <param name="itemIndexes">
    /// Optional ascending 1-based indexes of the eligible work items. Its count must equal <paramref name="totalItems"/>.
    /// Null means work items 1 through <paramref name="totalItems"/> are all eligible.
    /// </param>
    /// <param name="batesSequence">Optional Bates sequence of the run, used to name each audited record as its Load File record is named.</param>
    public ChaosEngine(
        long totalItems,
        string? chaosAmount,
        string? chaosTypes,
        int? seed = null,
        IReadOnlyList<long>? itemIndexes = null,
        BatesSequence? batesSequence = null)
    {
        this.format = LoadFileFormat.Dat;
        this.totalLines = totalItems;
        ChaosSampler.ValidateTotalLines(totalItems);
        if (itemIndexes is not null && itemIndexes.Count != totalItems)
        {
            throw new ArgumentException("The item index count must equal totalItems.", nameof(itemIndexes));
        }

#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        Random random = seed.HasValue ? new Random(seed.Value) : new Random();
#pragma warning restore S2245

        this.emailSeed = seed ?? random.Next();
        this.emailBatesSequence = batesSequence;
        this.enabledTypes = ResolveEnabledEmailTypes(chaosTypes);
        var emailTypes = this.enabledTypes.OrderBy(type => type, StringComparer.Ordinal).ToArray();

        this.sampler = new ChaosSampler(totalItems, chaosAmount, random);
        long[] sampledOrdinals = emailTypes.Length == 0 ? [] : this.sampler.TargetLines.Order().ToArray();

        // Ordinals are ascending, so audit slots follow work item order. Types rotate over the sampled items.
        this.emailSamples = new Dictionary<long, EmailSample>(sampledOrdinals.Length);
        this.emailAnomalies = new ChaosAnomaly?[sampledOrdinals.Length];
        for (int slot = 0; slot < sampledOrdinals.Length; slot++)
        {
            long ordinal = sampledOrdinals[slot];
            long workItemIndex = itemIndexes is null ? ordinal : itemIndexes[(int)(ordinal - 1)];
            this.emailSamples.Add(workItemIndex, new EmailSample(slot, emailTypes[slot % emailTypes.Length]));
        }
    }

    private static HashSet<string> ResolveEnabledEmailTypes(string? chaosTypes)
    {
        if (!string.IsNullOrEmpty(chaosTypes))
        {
            var resolvedTypes = new HashSet<string>(
                chaosTypes.Split(',').Select(type => ChaosAnomalyTypes.CanonicalEmailType(type.Trim().ToLowerInvariant())),
                StringComparer.OrdinalIgnoreCase);
            resolvedTypes.IntersectWith(EmailChaosTypes);
            return resolvedTypes;
        }

        return new HashSet<string>(EmailChaosTypes, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResolveEnabledTypes(LoadFileFormat format, string? chaosTypes)
    {
        var validTypes = format switch
        {
            LoadFileFormat.Opt => OptChaosTypes,
            LoadFileFormat.Csv => CsvChaosTypes,
            LoadFileFormat.Concordance => ConcordanceChaosTypes,
            LoadFileFormat.EdrmXml => EdrmXmlChaosTypes,
            _ => DatChaosTypes,
        };
        if (!string.IsNullOrEmpty(chaosTypes))
        {
            var resolvedTypes = new HashSet<string>(
                chaosTypes.Split(',').Select(type => type.Trim().ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
            resolvedTypes.IntersectWith(validTypes);
            return resolvedTypes;
        }

        // Preserve the existing empty-string behavior: an explicit empty DAT filter
        // enables every DAT anomaly, including the opt-in record-level types.
        if (chaosTypes is null)
        {
            var defaultTypes = format switch
            {
                LoadFileFormat.Opt => OptDefaultChaosTypes,
                LoadFileFormat.Csv => CsvChaosTypes,
                LoadFileFormat.Concordance => ConcordanceChaosTypes,
                LoadFileFormat.EdrmXml => EdrmXmlChaosTypes,
                _ => DatDefaultChaosTypes,
            };
            return new HashSet<string>(defaultTypes, StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(validTypes, StringComparer.OrdinalIgnoreCase);
    }

    private static long GetTargetableLines(long totalLines, bool onlyOptEncoding, bool onlyCsvBom, bool hasDatRecordOnlyTypes)
    {
        if (hasDatRecordOnlyTypes)
        {
            return Math.Max(1, totalLines - 1);
        }

        return (onlyOptEncoding || onlyCsvBom) && totalLines > 1 ? totalLines - 1 : totalLines;
    }

    private static IAnomalyApplier CreateApplier(
        LoadFileFormat format,
        Random random,
        string columnDelimiter,
        string quoteDelimiter,
        string eol,
        IReadOnlyCollection<string>? dateColumnNames,
        IReadOnlySet<string>? sourceFilePaths,
        IReadOnlySet<int>? dateColumnIndexes)
    {
        if (format == LoadFileFormat.Opt)
        {
            return new OptAnomalyApplier(random);
        }

        if (format == LoadFileFormat.Csv)
        {
            return new CsvAnomalyApplier(random);
        }

        if (format == LoadFileFormat.Concordance)
        {
            char fieldDelim = string.IsNullOrEmpty(columnDelimiter) ? '\u0014' : columnDelimiter[0];
            char quoteDelim = string.IsNullOrEmpty(quoteDelimiter) ? '\u00fe' : quoteDelimiter[0];
            return new ConcordanceAnomalyApplier(random, fieldDelim, quoteDelim);
        }

        if (format == LoadFileFormat.EdrmXml)
        {
            // EDRM-XML is the documented carve-out: XmlLoadFileWriter applies XML anomalies
            // itself via ApplyXmlChaos because the output is a document tree, not lines.
            return new XmlChaosAnomalyApplier();
        }

        return new DatAnomalyApplier(
            random,
            columnDelimiter,
            quoteDelimiter,
            string.IsNullOrEmpty(eol) ? "\r\n" : eol,
            dateColumnNames,
            sourceFilePaths,
            dateColumnIndexes);
    }

    /// <summary>
    /// Gets the list of injected anomalies for audit output.
    /// </summary>
    public IReadOnlyList<ChaosAnomaly> Anomalies => this.emailAnomalies is null
        ? this.anomalies
        : this.emailAnomalies.OfType<ChaosAnomaly>().ToList();

    /// <summary>
    /// Determines if a line should be intercepted by the chaos engine.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <returns>True if the line should be corrupted.</returns>
    public bool ShouldIntercept(long lineNumber)
    {
        if (this.hasDatRecordOnlyTypes)
        {
            return !this.noDatRecords
                && lineNumber > 1
                && lineNumber <= this.totalLines
                && this.sampler.ShouldIntercept(lineNumber - 1);
        }

        if (this.onlyCsvBom)
        {
            return this.totalLines > 1 && lineNumber < this.totalLines && this.sampler.ShouldIntercept(lineNumber);
        }

        return !(this.onlyOptEncoding && lineNumber >= this.totalLines) && this.sampler.ShouldIntercept(lineNumber);
    }

    /// <summary>
    /// Intercepts and corrupts a line. Returns the modified line.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="line">Original line content.</param>
    /// <param name="recordId">Record ID (e.g., "DOC00001054" or "HEADER").</param>
    /// <param name="record">Optional parsed record. DAT record-level anomalies require it.</param>
    /// <param name="serializer">Optional serializer used to render the modified record.</param>
    /// <returns>Modified line with injected anomaly.</returns>
    public string Intercept(
        long lineNumber,
        string line,
        string recordId,
        LoadFileRecord? record = null,
        ILoadFileSerializer? serializer = null)
    {
        if (this.enabledTypes.Count == 0 || this.applier is null)
        {
            return line;
        }

        var typeList = this.enabledTypes.ToArray();
        var chosenType = typeList[this.anomalyTypeIndex % typeList.Length];
        this.anomalyTypeIndex++;

        if (this.format == LoadFileFormat.Dat && record is null && DatRecordOnlyChaosTypes.Contains(chosenType))
        {
            var fallbackType = typeList.FirstOrDefault(type => !DatRecordOnlyChaosTypes.Contains(type));
            if (fallbackType is null)
            {
                return line;
            }

            chosenType = fallbackType;
        }

        if (this.format == LoadFileFormat.Opt && lineNumber == this.totalLines && chosenType == "opt-encoding")
        {
            if (this.onlyOptEncoding)
            {
                return line;
            }

            chosenType = typeList.First(type => type != "opt-encoding");
        }

        if (this.format == LoadFileFormat.Csv && lineNumber == this.totalLines && chosenType == "csv-bom")
        {
            if (this.onlyCsvBom)
            {
                return line;
            }

            chosenType = typeList.First(type => type != "csv-bom");
        }

        return this.applier.Apply(lineNumber, line, recordId, chosenType, this.anomalies, record, serializer);
    }

    /// <summary>
    /// Applies EDRM-XML structural anomalies to a fully rendered XML document inside the
    /// <see cref="LoadFiles.XmlLoadFileWriter"/> carve-out. Interception is sampled per
    /// <c>&lt;Document&gt;</c> element, mirroring the line sampling used by delimited formats.
    /// </summary>
    /// <param name="xml">The rendered XML document.</param>
    /// <returns>The XML document with any sampled anomalies applied.</returns>
    public string ApplyXmlChaos(string xml)
    {
        if (this.enabledTypes.Count == 0 || this.applier is not XmlChaosAnomalyApplier xmlApplier)
        {
            return xml;
        }

        var typeList = this.enabledTypes.OrderBy(type => type, StringComparer.Ordinal).ToArray();
        var builder = new StringBuilder(xml.Length + 64);
        long documentIndex = 0;
        int searchFrom = 0;
        bool encodingLieRequested = false;

        while (true)
        {
            int start = xml.IndexOf("<Document ", searchFrom, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            int end = xml.IndexOf("</Document>", start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            documentIndex++;
            builder.Append(xml, searchFrom, start - searchFrom);

            int documentEnd = end + "</Document>".Length;
            string documentXml = xml.Substring(start, documentEnd - start);

            if (this.ShouldIntercept(documentIndex))
            {
                string chosenType = typeList[this.anomalyTypeIndex % typeList.Length];
                this.anomalyTypeIndex++;

                if (chosenType == "xml-encoding-lie")
                {
                    encodingLieRequested = true;
                }
                else if (xmlApplier.TryApplyToDocument(documentXml, chosenType, out string mutated, out string column, out string description))
                {
                    string recordId = GetXmlRecordId(documentXml);
                    documentXml = mutated;
                    this.anomalies.Add(new ChaosAnomaly
                    {
                        LineNumber = documentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        RecordID = recordId,
                        Column = column,
                        ErrorType = chosenType,
                        Description = description,
                    });
                }
            }

            builder.Append(documentXml);
            searchFrom = documentEnd;
        }

        builder.Append(xml, searchFrom, xml.Length - searchFrom);
        string result = builder.ToString();

        if (encodingLieRequested && XmlChaosAnomalyApplier.TryApplyEncodingLie(result, out string rewritten))
        {
            result = rewritten;
            this.anomalies.Add(new ChaosAnomaly
            {
                LineNumber = "1",
                RecordID = "N/A",
                Column = "XML declaration",
                ErrorType = "xml-encoding-lie",
                Description = "Changed the XML declaration to claim UTF-16 while the emitted bytes remain UTF-8.",
            });
        }

        return result;
    }

    private static string GetXmlRecordId(string documentXml)
    {
        const string marker = "DocID=\"";
        int markerIndex = documentXml.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return "N/A";
        }

        int start = markerIndex + marker.Length;
        int end = documentXml.IndexOf('"', start);
        return end < 0 ? "N/A" : documentXml[start..end];
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
        if (this.format == LoadFileFormat.Csv && nextLineNumber > this.totalLines)
        {
            return null;
        }

        return this.applier?.GetEncodingAnomaly(lineNumber, nextLineNumber, encoding, this.anomalies);
    }

    /// <summary>
    /// Applies the email anomaly sampled for a work item and records its audit entry.
    /// Returns the content unchanged when the work item was not sampled.
    /// </summary>
    /// <remarks>
    /// The anomaly depends only on the seed, the work item index, and the content, so the output and audit
    /// match regardless of call order or concurrency. Each work item must be applied at most once.
    /// </remarks>
    public GeneratedFileContent ApplyEmailChaos(
        FileWorkItem workItem,
        GeneratedFileContent generated)
    {
        if (this.emailSamples is null || !this.emailSamples.TryGetValue(workItem.Index, out var sample))
        {
            return generated;
        }

        var result = EmailAnomalyApplier.Apply(
            generated.Content,
            workItem.FileName,
            DatComposerShared.ResolveParentId(workItem, this.emailBatesSequence),
            sample.Type,
            workItem.Index,
            EmailAnomalyApplier.CreateItemRandom(this.emailSeed, workItem.Index));
        this.emailAnomalies![sample.Slot] = result.Anomaly;

        return new GeneratedFileContent
        {
            Content = result.Content,
            Attachment = generated.Attachment,
            Attachments = generated.Attachments,
            Email = generated.Email,
            PageCount = generated.PageCount,
        };
    }

    /// <summary>
    /// Applies coordinated cross-format anomalies across DAT and OPT load file line buffers.
    /// </summary>
    /// <param name="datLines">The rendered DAT load file lines.</param>
    /// <param name="optLines">The rendered OPT load file lines.</param>
    /// <param name="scenarioOrTypes">Chaos scenario name or comma-separated anomaly types.</param>
    /// <param name="chaosAmount">Optional chaos amount percentage or count.</param>
    /// <param name="seed">Optional random seed for deterministic reproduction.</param>
    /// <param name="columnDelimiter">Configured DAT column delimiter.</param>
    /// <param name="quoteDelimiter">Configured DAT quote delimiter.</param>
    /// <param name="datFileName">File name for DAT audit references.</param>
    /// <param name="optFileName">File name for OPT audit references.</param>
    /// <param name="newlineDelimiter">Configured in-field newline delimiter used when re-rendering a mutated DAT record.</param>
    /// <returns>Result containing mutated lines and audited anomalies for both files.</returns>
    public static CrossFormatAnomalyResult ApplyCrossFormat(
        IReadOnlyList<string> datLines,
        IReadOnlyList<string> optLines,
        string? scenarioOrTypes,
        string? chaosAmount,
        int? seed = null,
        string columnDelimiter = "\u0014",
        string quoteDelimiter = "\u00fe",
        string datFileName = "loadfile.dat",
        string optFileName = "loadfile.opt",
        string newlineDelimiter = "\u00ae")
    {
#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        Random random = seed.HasValue ? new Random(seed.Value) : new Random();
#pragma warning restore S2245

        return CrossFormatAnomalyApplier.Apply(
            datLines,
            optLines,
            scenarioOrTypes,
            chaosAmount,
            random,
            columnDelimiter,
            quoteDelimiter,
            datFileName,
            optFileName,
            newlineDelimiter);
    }
}
