using System.Globalization;
using Zipper.LoadFiles;
using Zipper.Validation;

namespace Zipper;

/// <summary>
/// Result of applying coordinated cross-format anomalies across DAT and OPT load files.
/// </summary>
internal sealed record CrossFormatAnomalyResult
{
    /// <summary>
    /// Gets the mutated DAT load file lines.
    /// </summary>
    public required IReadOnlyList<string> DatLines { get; init; }

    /// <summary>
    /// Gets the mutated OPT load file lines.
    /// </summary>
    public required IReadOnlyList<string> OptLines { get; init; }

    /// <summary>
    /// Gets the audited anomalies injected into the DAT load file.
    /// </summary>
    public required IReadOnlyList<ChaosAnomaly> DatAnomalies { get; init; }

    /// <summary>
    /// Gets the audited anomalies injected into the OPT load file.
    /// </summary>
    public required IReadOnlyList<ChaosAnomaly> OptAnomalies { get; init; }

    /// <summary>
    /// Gets all audited cross-format anomalies across both load files.
    /// </summary>
    public IReadOnlyList<ChaosAnomaly> Anomalies => DatAnomalies.Concat(OptAnomalies).ToList();
}

/// <summary>
/// Coordinates anomalies that span multiple load files in Loadfile-Only or Production Set mode (DAT and OPT),
/// breaking cross-file invariants such as page count consistency, document sort order,
/// volume-relative image path alignment, and family relationships (orphaned attachments,
/// missing children, broken parent pointers).
/// </summary>
internal static class CrossFormatAnomalyApplier
{
    private const string FirstPageSuffix = "_001";

    private sealed record DatRecordEntry(
        long LineNumber,
        List<string> Fields,
        string DocId,
        string Bates,
        string ImagePath)
    {
        public DatRecordEntry(
            long lineNumber,
            List<string> fields,
            string docId,
            string bates,
            string imagePath,
            string parentId)
            : this(lineNumber, fields, docId, bates, imagePath)
        {
            this.ParentId = parentId;
            this.IsFamilyCandidate = !string.IsNullOrEmpty(parentId);
        }

        public string OriginalLine { get; init; } = string.Empty;

        public string ParentId { get; set; } = string.Empty;

        public int ActiveIndex { get; set; } = -1;

        public bool IsFamilyCandidate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this record's fields were mutated and must be re-rendered.
        /// </summary>
        public bool IsDirty { get; set; }
    }

    private sealed class ActiveDatRecords
    {
        private readonly List<int> activeRecordIndexes;

        public ActiveDatRecords(List<DatRecordEntry> originalOrder)
        {
            this.OriginalOrder = originalOrder;
            this.activeRecordIndexes = new List<int>(originalOrder.Count);
            for (int i = 0; i < originalOrder.Count; i++)
            {
                originalOrder[i].ActiveIndex = i;
                this.activeRecordIndexes.Add(i);
            }
        }

        public List<DatRecordEntry> OriginalOrder { get; }

        public int Count => this.activeRecordIndexes.Count;

        public DatRecordEntry this[int index] => this.OriginalOrder[this.activeRecordIndexes[index]];

        public bool IsActive(DatRecordEntry record) => record.ActiveIndex >= 0;

        public void Remove(DatRecordEntry record)
        {
            int activeIndex = record.ActiveIndex;
            if (activeIndex < 0)
            {
                return;
            }

            int lastActiveIndex = this.activeRecordIndexes.Count - 1;
            int swappedRecordIndex = this.activeRecordIndexes[lastActiveIndex];
            this.activeRecordIndexes[activeIndex] = swappedRecordIndex;
            this.OriginalOrder[swappedRecordIndex].ActiveIndex = activeIndex;
            this.activeRecordIndexes.RemoveAt(lastActiveIndex);
            record.ActiveIndex = -1;
        }
    }

    private sealed class OptDocEntry
    {
        public required string DocKey { get; init; }
        public required long BreakLineNumber { get; set; }
        public required string Volume { get; set; }
        public required string FirstImagePath { get; set; }
        public List<(long LineNumber, string Line, string[] Fields)> PageRows { get; } = [];
    }

    /// <summary>
    /// Applies coordinated cross-format anomalies across DAT and OPT load file lines with an optional seed.
    /// </summary>
    public static CrossFormatAnomalyResult Apply(
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
#pragma warning disable S2245 // Pseudo-randomness is safe for mock load-file anomaly injection
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
#pragma warning restore S2245
        return Apply(datLines, optLines, scenarioOrTypes, chaosAmount, random, columnDelimiter, quoteDelimiter, datFileName, optFileName, newlineDelimiter);
    }

    /// <summary>
    /// Applies coordinated cross-format anomalies across DAT and OPT load file lines.
    /// </summary>
    public static CrossFormatAnomalyResult Apply(
        IReadOnlyList<string> datLines,
        IReadOnlyList<string> optLines,
        string? scenarioOrTypes,
        string? chaosAmount,
        Random random,
        string columnDelimiter = "\u0014",
        string quoteDelimiter = "\u00fe",
        string datFileName = "loadfile.dat",
        string optFileName = "loadfile.opt",
        string newlineDelimiter = "\u00ae")
    {
        ArgumentNullException.ThrowIfNull(datLines);
        ArgumentNullException.ThrowIfNull(optLines);
        ArgumentNullException.ThrowIfNull(random);

        if (datLines.Count <= 1 || optLines.Count == 0)
        {
            return new CrossFormatAnomalyResult
            {
                DatLines = datLines,
                OptLines = optLines,
                DatAnomalies = Array.Empty<ChaosAnomaly>(),
                OptAnomalies = Array.Empty<ChaosAnomaly>(),
            };
        }

        var colDelimChar = string.IsNullOrEmpty(columnDelimiter) ? '\u0014' : columnDelimiter[0];
        var quoteDelimChar = string.IsNullOrEmpty(quoteDelimiter) ? '\0' : quoteDelimiter[0];

        var headerLine = datLines[0];
        var headers = ProductionSetPostValidator.ParseDatLine(headerLine, colDelimChar, quoteDelimChar);
        int originalHeaderCount = headers.Count;

        int docIdIdx = headers.FindIndex(h => string.Equals(h, "DOCID", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "Control Number", StringComparison.OrdinalIgnoreCase));
        int batesIdx = headers.FindIndex(h => string.Equals(h, "BATES_NUMBER", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "BATES", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "Bates Number", StringComparison.OrdinalIgnoreCase));
        int imageIdx = headers.FindIndex(h => string.Equals(h, "IMAGE_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "IMAGE", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "Image Path", StringComparison.OrdinalIgnoreCase));
        int pageCountIdx = headers.FindIndex(h => string.Equals(h, "PAGECOUNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGE_COUNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGE COUNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGECNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGE_CNT", StringComparison.OrdinalIgnoreCase));
        int parentIdIdx = FindParentIdColumn(headers);
        int endAttachIdx = FindEndAttachColumn(headers);

        var parsedDatRecords = new List<DatRecordEntry>(datLines.Count - 1);
        for (int i = 1; i < datLines.Count; i++)
        {
            var line = datLines[i];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var fields = ProductionSetPostValidator.ParseDatLine(line, colDelimChar, quoteDelimChar);
            string docId = docIdIdx >= 0 && docIdIdx < fields.Count ? fields[docIdIdx] : string.Empty;
            string bates = batesIdx >= 0 && batesIdx < fields.Count ? fields[batesIdx] : string.Empty;
            string img = imageIdx >= 0 && imageIdx < fields.Count ? fields[imageIdx] : string.Empty;
            string parent = parentIdIdx >= 0 && parentIdIdx < fields.Count ? fields[parentIdIdx] : string.Empty;

            if (string.IsNullOrEmpty(docId) && !string.IsNullOrEmpty(bates))
            {
                docId = bates;
            }

            parsedDatRecords.Add(new DatRecordEntry(i + 1, fields, docId, bates, img, parent) { OriginalLine = line });
        }

        var datRecords = new ActiveDatRecords(parsedDatRecords);
        var optDocs = new List<OptDocEntry>();
        OptDocEntry? currentOptDoc = null;
        for (int j = 0; j < optLines.Count; j++)
        {
            var line = optLines[j];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cols = line.Split(',');
            string key = cols.Length > 0 ? cols[0].Trim() : string.Empty;
            string vol = cols.Length > 1 ? cols[1].Trim() : string.Empty;
            string img = cols.Length > 2 ? cols[2].Trim() : string.Empty;
            bool isBreak = cols.Length > 3 && (string.Equals(cols[3].Trim(), "Y", StringComparison.OrdinalIgnoreCase) || cols[3].Trim() == "1");

            if (isBreak || currentOptDoc is null)
            {
                currentOptDoc = new OptDocEntry
                {
                    DocKey = key,
                    BreakLineNumber = j + 1,
                    Volume = vol,
                    FirstImagePath = img,
                };
                optDocs.Add(currentOptDoc);
            }

            currentOptDoc.PageRows.Add((j + 1, line, cols));
        }

        var datRecordsByKey = BuildDatRecordIndex(datRecords.OriginalOrder, includeBates: true);
        var datRecordsByDocId = BuildDatRecordIndex(datRecords.OriginalOrder, includeBates: false);
        var optDocsByKey = BuildOptDocumentIndex(optDocs);
        var familyCandidates = datRecords.OriginalOrder
            .Where(record => record.IsFamilyCandidate)
            .ToList();
        int familyCandidateIndex = 0;
        var candidateTypes = ResolveCandidateTypes(scenarioOrTypes);
        var scenario = string.IsNullOrEmpty(scenarioOrTypes) ? null : ChaosScenarios.GetByName(scenarioOrTypes);
        string? resolvedAmount = string.IsNullOrEmpty(chaosAmount) ? scenario?.DefaultAmount : chaosAmount;
        int totalDatDocs = datRecords.Count;
        int targetCount = ChaosSampler.ParseChaosAmount(resolvedAmount, totalDatDocs);
        if (targetCount <= 0)
        {
            return new CrossFormatAnomalyResult
            {
                DatLines = datLines,
                OptLines = optLines,
                DatAnomalies = Array.Empty<ChaosAnomaly>(),
                OptAnomalies = Array.Empty<ChaosAnomaly>(),
            };
        }

        var datAnomalies = new List<ChaosAnomaly>();
        var optAnomalies = new List<ChaosAnomaly>();
        var targetedRecords = new HashSet<DatRecordEntry>(ReferenceEqualityComparer.Instance);

        int typeIndex = 0;
        for (int k = 0; k < targetCount; k++)
        {
            string chosenType = candidateTypes[typeIndex % candidateTypes.Count];
            typeIndex++;

            switch (chosenType)
            {
                case "desync-pagecount":
                    ApplyDesyncPagecount(
                        datRecords,
                        datRecordsByKey,
                        optDocs,
                        optDocsByKey,
                        targetedRecords,
                        random,
                        datAnomalies,
                        optAnomalies,
                        datFileName,
                        optFileName,
                        pageCountIdx >= 0 ? headers[pageCountIdx] : "PAGECOUNT");
                    break;
                case "desync-order":
                    ApplyDesyncOrder(
                        datRecords,
                        datRecordsByKey,
                        optDocs,
                        targetedRecords,
                        random,
                        datAnomalies,
                        optAnomalies,
                        datFileName,
                        optFileName,
                        batesIdx >= 0 ? headers[batesIdx] : (docIdIdx >= 0 ? headers[docIdIdx] : "DOCID"));
                    break;
                case "desync-path":
                    ApplyDesyncPath(
                        datRecords,
                        datRecordsByKey,
                        optDocs,
                        targetedRecords,
                        random,
                        datAnomalies,
                        optAnomalies,
                        datFileName,
                        optFileName,
                        imageIdx >= 0 ? headers[imageIdx] : "IMAGE_PATH");
                    break;
                case "orphan-attachment":
                    ApplyOrphanAttachment(
                        datRecords,
                        familyCandidates,
                        ref familyCandidateIndex,
                        targetedRecords,
                        datRecordsByDocId,
                        optDocsByKey,
                        optDocs,
                        random,
                        datAnomalies,
                        optAnomalies,
                        datFileName,
                        optFileName,
                        headers,
                        ref parentIdIdx);
                    break;
                case "missing-child":
                    ApplyMissingChild(
                        datRecords,
                        familyCandidates,
                        ref familyCandidateIndex,
                        targetedRecords,
                        datRecordsByDocId,
                        optDocsByKey,
                        optDocs,
                        random,
                        datAnomalies,
                        optAnomalies,
                        datFileName,
                        optFileName,
                        headers,
                        ref endAttachIdx);
                    break;
                case "broken-parent":
                    ApplyBrokenParent(
                        datRecords,
                        familyCandidates,
                        ref familyCandidateIndex,
                        targetedRecords,
                        datRecordsByDocId,
                        optDocsByKey,
                        optDocs,
                        random,
                        datAnomalies,
                        optAnomalies,
                        datFileName,
                        optFileName,
                        headers,
                        ref parentIdIdx);
                    break;
            }
        }

        // Reconstruct DAT lines, preserving the original bytes of untouched records.
        bool headerChanged = headers.Count != originalHeaderCount;
        var datSerializer = new DatSerializer(colDelimChar, quoteDelimChar, newlineDelimiter);
        var outDatLines = new List<string>(datRecords.Count + 1)
        {
            headerChanged ? datSerializer.RenderHeader(headers) : headerLine,
        };
        foreach (var rec in datRecords.OriginalOrder)
        {
            if (!datRecords.IsActive(rec))
            {
                continue;
            }

            outDatLines.Add(headerChanged || rec.IsDirty
                ? datSerializer.RenderRecord(new LoadFileRecord
                {
                    Columns = headers,
                    Values = rec.Fields,
                    RecordId = rec.DocId,
                })
                : rec.OriginalLine);
        }

        // Reconstruct OPT lines
        var outOptLines = new List<string>();
        foreach (var doc in optDocs)
        {
            foreach (var row in doc.PageRows)
            {
                outOptLines.Add(row.Line);
            }
        }

        if (datAnomalies.Count > 0 || optAnomalies.Count > 0)
        {
            long maxDatLineNumber = Math.Max(2L, (long)outDatLines.Count);
            long maxOptLineNumber = Math.Max(1L, (long)outOptLines.Count);
            var datLineNumbers = BuildDatLineNumbers(datRecords, docIdIdx, batesIdx, maxDatLineNumber);
            var optLineNumbers = BuildOptLineNumbers(optDocs, maxOptLineNumber);
            UpdateAnomalyLineNumbers(
                datAnomalies,
                datFileName,
                optFileName,
                datLineNumbers,
                optLineNumbers,
                maxDatLineNumber,
                maxOptLineNumber);
            UpdateAnomalyLineNumbers(
                optAnomalies,
                datFileName,
                optFileName,
                datLineNumbers,
                optLineNumbers,
                maxDatLineNumber,
                maxOptLineNumber);
        }

        return new CrossFormatAnomalyResult
        {
            DatLines = outDatLines,
            OptLines = outOptLines,
            DatAnomalies = datAnomalies,
            OptAnomalies = optAnomalies,
        };
    }

    private static Dictionary<string, long> BuildDatLineNumbers(
        ActiveDatRecords records,
        int docIdIndex,
        int batesIndex,
        long maxLineNumber)
    {
        var lineNumbers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long lineNumber = 2;
        foreach (var record in records.OriginalOrder)
        {
            if (records.IsActive(record))
            {
                long effectiveLineNumber = Math.Min(lineNumber, maxLineNumber);
                AddLineNumber(lineNumbers, record.Fields, docIdIndex, effectiveLineNumber);
                AddLineNumber(lineNumbers, record.Fields, batesIndex, effectiveLineNumber);
                lineNumber++;
            }
        }

        return lineNumbers;
    }

    private static Dictionary<string, long> BuildOptLineNumbers(IReadOnlyList<OptDocEntry> documents, long maxLineNumber)
    {
        var lineNumbers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long lineNumber = 1;
        foreach (var document in documents)
        {
            foreach (var row in document.PageRows)
            {
                if (row.Fields.Length > 0 && !string.IsNullOrEmpty(row.Fields[0]))
                {
                    long effectiveLineNumber = Math.Min(lineNumber, maxLineNumber);
                    lineNumbers.TryAdd(row.Fields[0].Trim(), effectiveLineNumber);
                }

                lineNumber++;
            }
        }

        return lineNumbers;
    }

    private static void AddLineNumber(Dictionary<string, long> lineNumbers, IReadOnlyList<string> fields, int fieldIndex, long lineNumber)
    {
        if (fieldIndex >= 0 && fieldIndex < fields.Count && !string.IsNullOrEmpty(fields[fieldIndex]))
        {
            lineNumbers.TryAdd(fields[fieldIndex], lineNumber);
        }
    }

    private static void UpdateAnomalyLineNumbers(
        List<ChaosAnomaly> anomalies,
        string datFileName,
        string optFileName,
        IReadOnlyDictionary<string, long> datLineNumbers,
        IReadOnlyDictionary<string, long> optLineNumbers,
        long datGapLineNumber,
        long optGapLineNumber)
    {
        for (int i = 0; i < anomalies.Count; i++)
        {
            var anomaly = anomalies[i];
            string? lineNumber = FindAuditLineNumber(
                anomaly.File,
                anomaly.RecordID,
                datFileName,
                optFileName,
                datLineNumbers,
                optLineNumbers);
            long anomalyGapLineNumber = string.Equals(anomaly.File, datFileName, StringComparison.OrdinalIgnoreCase)
                ? datGapLineNumber
                : optGapLineNumber;
            var peer = anomaly.Peer;
            if (peer is not null)
            {
                string? peerLineNumber = FindAuditLineNumber(
                    peer.File,
                    peer.RecordID,
                    datFileName,
                    optFileName,
                    datLineNumbers,
                    optLineNumbers);
                long peerGapLineNumber = string.Equals(peer.File, datFileName, StringComparison.OrdinalIgnoreCase)
                    ? datGapLineNumber
                    : optGapLineNumber;
                peer = peer with
                {
                    LineNumber = peerLineNumber ?? peerGapLineNumber.ToString(CultureInfo.InvariantCulture),
                };
            }

            anomalies[i] = anomaly with
            {
                LineNumber = lineNumber ?? anomalyGapLineNumber.ToString(CultureInfo.InvariantCulture),
                Peer = peer,
            };
        }
    }

    private static string? FindAuditLineNumber(
        string? file,
        string recordId,
        string datFileName,
        string optFileName,
        IReadOnlyDictionary<string, long> datLineNumbers,
        IReadOnlyDictionary<string, long> optLineNumbers)
    {
        IReadOnlyDictionary<string, long>? lineNumbers = string.Equals(file, datFileName, StringComparison.OrdinalIgnoreCase)
            ? datLineNumbers
            : string.Equals(file, optFileName, StringComparison.OrdinalIgnoreCase)
                ? optLineNumbers
                : null;
        return lineNumbers is not null && lineNumbers.TryGetValue(recordId, out long lineNumber)
            ? lineNumber.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static IReadOnlyList<string> ResolveCandidateTypes(string? scenarioOrTypes)
    {
        if (string.IsNullOrEmpty(scenarioOrTypes))
        {
            return ChaosAnomalyTypes.CrossFormat.All;
        }

        var scenario = ChaosScenarios.GetByName(scenarioOrTypes);
        if (scenario is not null)
        {
            return string.IsNullOrEmpty(scenario.ChaosTypes)
                ? ChaosAnomalyTypes.CrossFormat.All
                : scenario.ChaosTypes.Split(',');
        }

        var split = scenarioOrTypes.Split(',')
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => ChaosAnomalyTypes.CrossFormat.All.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return split.Count > 0 ? split : ChaosAnomalyTypes.CrossFormat.All;
    }

    private static (OptDocEntry OptDoc, DatRecordEntry DatDoc)? FindRandomUnprocessedOptDocument(
        IReadOnlyList<OptDocEntry> optDocs,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByKey,
        ActiveDatRecords datRecords,
        HashSet<DatRecordEntry> targetedRecords,
        Random random)
    {
        if (optDocs.Count == 0 || datRecords.Count == 0)
        {
            return null;
        }

        int start = random.Next(optDocs.Count);
        for (int offset = 0; offset < optDocs.Count; offset++)
        {
            int index = (start + offset) % optDocs.Count;
            var optDoc = optDocs[index];
            DatRecordEntry? datDoc;
            if (FindMatchingDatRecord(datRecordsByKey, optDoc.DocKey) is { } mappedRecord)
            {
                if (!datRecords.IsActive(mappedRecord))
                {
                    continue;
                }

                datDoc = mappedRecord;
            }
            else
            {
                datDoc = FindRandomUnprocessedDatRecord(datRecords, targetedRecords, random);
                if (datDoc is null)
                {
                    return null;
                }
            }

            if (!targetedRecords.Contains(datDoc))
            {
                return (optDoc, datDoc);
            }
        }

        return null;
    }

    private static DatRecordEntry? FindRandomUnprocessedDatRecord(
        ActiveDatRecords datRecords,
        HashSet<DatRecordEntry> targetedRecords,
        Random random)
    {
        if (datRecords.Count == 0)
        {
            return null;
        }

        int start = random.Next(datRecords.Count);
        for (int offset = 0; offset < datRecords.Count; offset++)
        {
            var candidate = datRecords[(start + offset) % datRecords.Count];
            if (!targetedRecords.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static DatRecordEntry? FindDatForOptDocument(
        OptDocEntry optDoc,
        int optDocumentIndex,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByKey,
        ActiveDatRecords datRecords)
    {
        if (FindMatchingDatRecord(datRecordsByKey, optDoc.DocKey) is { } mappedRecord)
        {
            return datRecords.IsActive(mappedRecord) ? mappedRecord : null;
        }

        return datRecords.Count == 0
            ? null
            : datRecords[Math.Min(optDocumentIndex, datRecords.Count - 1)];
    }

    private static void ApplyDesyncPagecount(
        ActiveDatRecords datRecords,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByKey,
        List<OptDocEntry> optDocs,
        IReadOnlyDictionary<string, OptDocEntry> optDocsByKey,
        HashSet<DatRecordEntry> targetedRecords,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string pageCountColumnName)
    {
        var target = FindRandomUnprocessedOptDocument(optDocs, datRecordsByKey, datRecords, targetedRecords, random);
        if (target is null)
        {
            return;
        }

        var (optDoc, datDoc) = target.Value;

        if (optDoc.PageRows.Count > 1)
        {
            // Drop continuation page row: page-count lie
            var droppedRow = optDoc.PageRows[^1];
            optDoc.PageRows.RemoveAt(optDoc.PageRows.Count - 1);

            datAnomalies.Add(new ChaosAnomaly
            {
                File = datFileName,
                LineNumber = datDoc.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = datDoc.DocId,
                Column = pageCountColumnName,
                ErrorType = "desync-pagecount",
                Description = $"Page count mismatch: OPT page row '{droppedRow.Fields[0]}' dropped, diverging from DAT page count declaration.",
                Peer = new ChaosAnomalyPeer
                {
                    File = optFileName,
                    LineNumber = optDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = optDoc.DocKey,
                    Column = "PageCount",
                },
            });

            optAnomalies.Add(new ChaosAnomaly
            {
                File = optFileName,
                LineNumber = optDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optDoc.DocKey,
                Column = "PageCount",
                ErrorType = "desync-pagecount",
                Description = $"Dropped OPT continuation page row '{droppedRow.Fields[0]}' for document '{optDoc.DocKey}', causing page-count lie against DAT.",
                Peer = new ChaosAnomalyPeer
                {
                    File = datFileName,
                    LineNumber = datDoc.LineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = datDoc.DocId,
                    Column = pageCountColumnName,
                },
            });
        }
        else
        {
            // Duplicate/add a continuation page row
            int pageNumber = 2;
            string extraKey = $"{optDoc.DocKey}_{pageNumber:D3}";
            while (optDocsByKey.ContainsKey(extraKey))
            {
                pageNumber++;
                extraKey = $"{optDoc.DocKey}_{pageNumber:D3}";
            }

            string firstImg = optDoc.FirstImagePath;
            var ext = Path.GetExtension(firstImg);
            string extraImg = $"{firstImg[..^ext.Length]}_{pageNumber:D3}{ext}";

            string extraLine = $"{extraKey},{optDoc.Volume},{extraImg},,,,";
            var extraCols = extraLine.Split(',');
            optDoc.PageRows.Add((optDoc.BreakLineNumber + 1, extraLine, extraCols));

            datAnomalies.Add(new ChaosAnomaly
            {
                File = datFileName,
                LineNumber = datDoc.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = datDoc.DocId,
                Column = pageCountColumnName,
                ErrorType = "desync-pagecount",
                Description = $"Page count mismatch: OPT extra page row '{extraKey}' added, diverging from DAT page count declaration.",
                Peer = new ChaosAnomalyPeer
                {
                    File = optFileName,
                    LineNumber = optDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = optDoc.DocKey,
                    Column = "PageCount",
                },
            });

            optAnomalies.Add(new ChaosAnomaly
            {
                File = optFileName,
                LineNumber = optDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optDoc.DocKey,
                Column = "PageCount",
                ErrorType = "desync-pagecount",
                Description = $"Duplicated OPT page row '{extraKey}' for document '{optDoc.DocKey}', causing page-count lie against DAT.",
                Peer = new ChaosAnomalyPeer
                {
                    File = datFileName,
                    LineNumber = datDoc.LineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = datDoc.DocId,
                    Column = pageCountColumnName,
                },
            });
        }

        targetedRecords.Add(datDoc);
    }

    private static void ApplyDesyncOrder(
        ActiveDatRecords datRecords,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByKey,
        List<OptDocEntry> optDocs,
        HashSet<DatRecordEntry> targetedRecords,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string batesColumnName)
    {
        if (optDocs.Count < 2 || datRecords.Count < 2)
        {
            return;
        }

        int pairCount = optDocs.Count - 1;
        int start = random.Next(pairCount);
        int idx1 = -1;
        int idx2 = -1;
        DatRecordEntry? datDoc1 = null;
        DatRecordEntry? datDoc2 = null;
        for (int offset = 0; offset < pairCount; offset++)
        {
            int candidateIndex = (start + offset) % pairCount;
            var candidateDoc1 = optDocs[candidateIndex];
            var candidateDoc2 = optDocs[candidateIndex + 1];
            var candidateDat1 = FindDatForOptDocument(candidateDoc1, candidateIndex, datRecordsByKey, datRecords);
            var candidateDat2 = FindDatForOptDocument(candidateDoc2, candidateIndex + 1, datRecordsByKey, datRecords);
            if (candidateDat1 is null
                || candidateDat2 is null
                || targetedRecords.Contains(candidateDat1)
                || targetedRecords.Contains(candidateDat2))
            {
                continue;
            }

            idx1 = candidateIndex;
            idx2 = candidateIndex + 1;
            datDoc1 = candidateDat1;
            datDoc2 = candidateDat2;
            break;
        }

        if (idx1 < 0 || datDoc1 is null || datDoc2 is null)
        {
            return;
        }

        var optDoc1 = optDocs[idx1];
        var optDoc2 = optDocs[idx2];

        // Swap documents in OPT
        optDocs[idx1] = optDoc2;
        optDocs[idx2] = optDoc1;

        long optDoc2LineNumber = optDoc2.BreakLineNumber;

        datAnomalies.Add(new ChaosAnomaly
        {
            File = datFileName,
            LineNumber = datDoc1.LineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = datDoc1.DocId,
            Column = batesColumnName,
            ErrorType = "desync-order",
            Description = $"Document sort order mismatch: DAT sequence maintains '{datDoc1.DocId}' before '{datDoc2.DocId}', but OPT order shifted.",
            Peer = new ChaosAnomalyPeer
            {
                File = optFileName,
                LineNumber = optDoc2LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optDoc2.DocKey,
                Column = "DocBreak",
            },
        });

        optAnomalies.Add(new ChaosAnomaly
        {
            File = optFileName,
            LineNumber = optDoc2LineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = optDoc2.DocKey,
            Column = "DocBreak",
            ErrorType = "desync-order",
            Description = $"Shifted OPT document '{optDoc2.DocKey}' ahead of '{optDoc1.DocKey}', breaking sort order alignment with DAT.",
            Peer = new ChaosAnomalyPeer
            {
                File = datFileName,
                LineNumber = datDoc1.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = datDoc1.DocId,
                Column = batesColumnName,
            },
        });

        targetedRecords.Add(datDoc1);
        targetedRecords.Add(datDoc2);
    }

    private static void ApplyDesyncPath(
        ActiveDatRecords datRecords,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByKey,
        List<OptDocEntry> optDocs,
        HashSet<DatRecordEntry> targetedRecords,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string imageColumnName)
    {
        var target = FindRandomUnprocessedOptDocument(optDocs, datRecordsByKey, datRecords, targetedRecords, random);
        if (target is null)
        {
            return;
        }

        var (optDoc, datDoc) = target.Value;

        // Point OPT image path to different volume, e.g. VOL999
        string originalPath = optDoc.FirstImagePath;
        string corruptPath = ReplaceVolumeInPath(originalPath, "VOL999");

        // Modify first page row
        if (optDoc.PageRows.Count > 0)
        {
            var firstRow = optDoc.PageRows[0];
            var cols = firstRow.Fields;
            if (cols.Length > 2)
            {
                cols[1] = "VOL999";
                cols[2] = corruptPath;
                string newLine = string.Join(",", cols);
                optDoc.PageRows[0] = (firstRow.LineNumber, newLine, cols);
                optDoc.FirstImagePath = corruptPath;
                optDoc.Volume = "VOL999";
            }
        }

        datAnomalies.Add(new ChaosAnomaly
        {
            File = datFileName,
            LineNumber = datDoc.LineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = datDoc.DocId,
            Column = imageColumnName,
            ErrorType = "desync-path",
            Description = $"Image path volume divergence: DAT specifies '{datDoc.ImagePath}', but OPT references mismatched volume 'VOL999'.",
            Peer = new ChaosAnomalyPeer
            {
                File = optFileName,
                LineNumber = optDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optDoc.DocKey,
                Column = "ImagePath",
            },
        });

        optAnomalies.Add(new ChaosAnomaly
        {
            File = optFileName,
            LineNumber = optDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = optDoc.DocKey,
            Column = "ImagePath",
            ErrorType = "desync-path",
            Description = $"Pointed OPT image path for '{optDoc.DocKey}' to mismatched volume path '{corruptPath}'.",
            Peer = new ChaosAnomalyPeer
            {
                File = datFileName,
                LineNumber = datDoc.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = datDoc.DocId,
                Column = imageColumnName,
            },
        });

        targetedRecords.Add(datDoc);
    }

    private static void ApplyOrphanAttachment(
        ActiveDatRecords datRecords,
        List<DatRecordEntry> familyCandidates,
        ref int familyCandidateIndex,
        HashSet<DatRecordEntry> targetedRecords,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByDocId,
        IReadOnlyDictionary<string, OptDocEntry> optDocsByKey,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        List<string> headers,
        ref int parentIdIndex)
    {
        if (datRecords.Count == 0 || optDocs.Count == 0)
        {
            return;
        }

        var childRec = FindFamilyCandidate(
            datRecords,
            familyCandidates,
            ref familyCandidateIndex,
            targetedRecords,
            datRecordsByDocId,
            requireActiveParent: true);
        if (childRec is not null
            && datRecordsByDocId.TryGetValue(childRec.ParentId, out var parentRec)
            && datRecords.IsActive(parentRec))
        {
            datRecords.Remove(parentRec);
            targetedRecords.Add(childRec);

            var optParent = FindMatchingOptForDat(optDocsByKey, parentRec)
                ?? optDocs[Math.Min(Math.Max(childRec.ActiveIndex, 0), optDocs.Count - 1)];
            string parentIdColumnName = parentIdIndex >= 0 ? headers[parentIdIndex] : "PARENTDOCID";

            datAnomalies.Add(new ChaosAnomaly
            {
                File = datFileName,
                LineNumber = childRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = childRec.DocId,
                Column = parentIdColumnName,
                ErrorType = "orphan-attachment",
                Description = $"Attachment row '{childRec.DocId}' has no parent row in DAT (parent '{parentRec.DocId}' dropped).",
                Peer = new ChaosAnomalyPeer
                {
                    File = optFileName,
                    LineNumber = optParent.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = optParent.DocKey,
                    Column = "DocBreak",
                },
            });

            optAnomalies.Add(new ChaosAnomaly
            {
                File = optFileName,
                LineNumber = optParent.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optParent.DocKey,
                Column = "DocBreak",
                ErrorType = "orphan-attachment",
                Description = $"Parent document '{optParent.DocKey}' retained in OPT while dropped from DAT, orphaning attachment child '{childRec.DocId}'.",
                Peer = new ChaosAnomalyPeer
                {
                    File = datFileName,
                    LineNumber = childRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = childRec.DocId,
                    Column = parentIdColumnName,
                },
            });

            return;
        }

        var targetRec = FindRandomUnprocessedDatRecord(datRecords, targetedRecords, random);
        if (targetRec is null)
        {
            return;
        }

        parentIdIndex = EnsureParentIdColumn(parentIdIndex, headers, datRecords);
        string orphanParentIdColumnName = headers[parentIdIndex];
        string missingParentId = "DOC_PARENT_MISSING";
        targetRec.ParentId = missingParentId;
        targetRec.Fields[parentIdIndex] = missingParentId;
        targetRec.IsDirty = true;

        var optPeer = FindMatchingOptForDat(optDocsByKey, targetRec)
            ?? optDocs[Math.Min(Math.Max(targetRec.ActiveIndex, 0), optDocs.Count - 1)];

        datAnomalies.Add(new ChaosAnomaly
        {
            File = datFileName,
            LineNumber = targetRec.LineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = targetRec.DocId,
            Column = orphanParentIdColumnName,
            ErrorType = "orphan-attachment",
            Description = $"Attachment row '{targetRec.DocId}' references non-existent parent '{missingParentId}'.",
            Peer = new ChaosAnomalyPeer
            {
                File = optFileName,
                LineNumber = optPeer.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optPeer.DocKey,
                Column = "DocBreak",
            },
        });

        optAnomalies.Add(new ChaosAnomaly
        {
            File = optFileName,
            LineNumber = optPeer.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = optPeer.DocKey,
            Column = "DocBreak",
            ErrorType = "orphan-attachment",
            Description = $"Orphaned family anomaly coordinated with DAT record '{targetRec.DocId}'.",
            Peer = new ChaosAnomalyPeer
            {
                File = datFileName,
                LineNumber = targetRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = targetRec.DocId,
                Column = orphanParentIdColumnName,
            },
        });

        targetedRecords.Add(targetRec);
    }

    private static void ApplyMissingChild(
        ActiveDatRecords datRecords,
        List<DatRecordEntry> familyCandidates,
        ref int familyCandidateIndex,
        HashSet<DatRecordEntry> targetedRecords,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByDocId,
        IReadOnlyDictionary<string, OptDocEntry> optDocsByKey,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        List<string> headers,
        ref int endAttachIndex)
    {
        if (datRecords.Count == 0 || optDocs.Count == 0)
        {
            return;
        }

        var childRec = FindFamilyCandidate(
            datRecords,
            familyCandidates,
            ref familyCandidateIndex,
            targetedRecords,
            datRecordsByDocId,
            requireActiveParent: false);
        if (childRec is not null)
        {
            var optChild = FindMatchingOptForDat(optDocsByKey, childRec);
            if (optChild is null)
            {
                targetedRecords.Add(childRec);
                familyCandidateIndex++;
                return;
            }

            datRecords.Remove(childRec);
            string familyEndAttachColumnName = endAttachIndex >= 0 ? headers[endAttachIndex] : "ENDATTACH";

            datAnomalies.Add(new ChaosAnomaly
            {
                File = datFileName,
                LineNumber = childRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = childRec.DocId,
                Column = familyEndAttachColumnName,
                ErrorType = "missing-child",
                Description = $"Parent document '{childRec.ParentId}' missing child attachment '{childRec.DocId}' in DAT.",
                Peer = new ChaosAnomalyPeer
                {
                    File = optFileName,
                    LineNumber = optChild.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = optChild.DocKey,
                    Column = "DocBreak",
                },
            });

            optAnomalies.Add(new ChaosAnomaly
            {
                File = optFileName,
                LineNumber = optChild.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optChild.DocKey,
                Column = "DocBreak",
                ErrorType = "missing-child",
                Description = $"Child attachment '{optChild.DocKey}' missing from DAT but present in OPT.",
                Peer = new ChaosAnomalyPeer
                {
                    File = datFileName,
                    LineNumber = childRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = childRec.DocId,
                    Column = familyEndAttachColumnName,
                },
            });

            targetedRecords.Add(childRec);
            return;
        }

        if (datRecords.Count < 2 || optDocs.Count < 2)
        {
            return;
        }

        var childRecord = FindRandomUnprocessedDatRecord(datRecords, targetedRecords, random);
        if (childRecord is null)
        {
            return;
        }

        var fallbackOptChild = FindMatchingOptForDat(optDocsByKey, childRecord);
        if (fallbackOptChild is null)
        {
            return;
        }

        var parentRecord = datRecords[0];
        if (ReferenceEquals(parentRecord, childRecord))
        {
            parentRecord = datRecords[1];
        }

        endAttachIndex = EnsureEndAttachColumn(endAttachIndex, headers, datRecords);
        string endAttachColumnName = headers[endAttachIndex];

        string childEndId = string.IsNullOrEmpty(childRecord.Bates) ? childRecord.DocId : childRecord.Bates;
        parentRecord.Fields[endAttachIndex] = childEndId;
        parentRecord.IsDirty = true;

        datRecords.Remove(childRecord);

        datAnomalies.Add(new ChaosAnomaly
        {
            File = datFileName,
            LineNumber = childRecord.LineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = childRecord.DocId,
            Column = endAttachColumnName,
            ErrorType = "missing-child",
            Description = $"Parent document '{parentRecord.DocId}' references missing DAT child '{childRecord.DocId}', which remains in OPT.",
            Peer = new ChaosAnomalyPeer
            {
                File = optFileName,
                LineNumber = fallbackOptChild.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = fallbackOptChild.DocKey,
                Column = "DocBreak",
            },
        });

        optAnomalies.Add(new ChaosAnomaly
        {
            File = optFileName,
            LineNumber = fallbackOptChild.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = fallbackOptChild.DocKey,
            Column = "DocBreak",
            ErrorType = "missing-child",
            Description = $"OPT child '{fallbackOptChild.DocKey}' remains present while its DAT record '{childRecord.DocId}' is missing.",
            Peer = new ChaosAnomalyPeer
            {
                File = datFileName,
                LineNumber = childRecord.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = childRecord.DocId,
                Column = endAttachColumnName,
            },
        });

        targetedRecords.Add(childRecord);
    }

    private static void ApplyBrokenParent(
        ActiveDatRecords datRecords,
        List<DatRecordEntry> familyCandidates,
        ref int familyCandidateIndex,
        HashSet<DatRecordEntry> targetedRecords,
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByDocId,
        IReadOnlyDictionary<string, OptDocEntry> optDocsByKey,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        List<string> headers,
        ref int parentIdIndex)
    {
        if (datRecords.Count == 0 || optDocs.Count == 0)
        {
            return;
        }

        var targetRec = FindFamilyCandidate(
            datRecords,
            familyCandidates,
            ref familyCandidateIndex,
            targetedRecords,
            datRecordsByDocId,
            requireActiveParent: false)
            ?? FindRandomUnprocessedDatRecord(datRecords, targetedRecords, random);
        if (targetRec is null)
        {
            return;
        }
        parentIdIndex = EnsureParentIdColumn(parentIdIndex, headers, datRecords);
        string parentIdColumnName = headers[parentIdIndex];
        string corruptParent = "INVALID_PARENT_PTR";
        targetRec.ParentId = corruptParent;
        targetRec.Fields[parentIdIndex] = corruptParent;
        targetRec.IsDirty = true;
        AddFamilyCandidate(targetRec, familyCandidates);

        var optPeer = FindMatchingOptForDat(optDocsByKey, targetRec)
            ?? optDocs[Math.Min(Math.Max(targetRec.ActiveIndex, 0), optDocs.Count - 1)];

        datAnomalies.Add(new ChaosAnomaly
        {
            File = datFileName,
            LineNumber = targetRec.LineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = targetRec.DocId,
            Column = parentIdColumnName,
            ErrorType = "broken-parent",
            Description = $"Broken parent pointer on child record '{targetRec.DocId}': PARENTDOCID corrupted to '{corruptParent}'.",
            Peer = new ChaosAnomalyPeer
            {
                File = optFileName,
                LineNumber = optPeer.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optPeer.DocKey,
                Column = "DocBreak",
            },
        });

        optAnomalies.Add(new ChaosAnomaly
        {
            File = optFileName,
            LineNumber = optPeer.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = optPeer.DocKey,
            Column = "DocBreak",
            ErrorType = "broken-parent",
            Description = $"Coordinated broken parent pointer for child record '{optPeer.DocKey}'.",
            Peer = new ChaosAnomalyPeer
            {
                File = datFileName,
                LineNumber = targetRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = targetRec.DocId,
                Column = parentIdColumnName,
            },
        });

        targetedRecords.Add(targetRec);
    }

    private static DatRecordEntry? FindFamilyCandidate(
        ActiveDatRecords records,
        IReadOnlyList<DatRecordEntry> candidates,
        ref int candidateIndex,
        HashSet<DatRecordEntry> targetedRecords,
        IReadOnlyDictionary<string, DatRecordEntry> recordsByDocId,
        bool requireActiveParent)
    {
        while (candidateIndex < candidates.Count)
        {
            var candidate = candidates[candidateIndex];
            if (!records.IsActive(candidate)
                || targetedRecords.Contains(candidate)
                || string.IsNullOrEmpty(candidate.ParentId))
            {
                candidateIndex++;
                continue;
            }

            if (requireActiveParent
                && (!recordsByDocId.TryGetValue(candidate.ParentId, out var parent)
                    || !records.IsActive(parent)
                    || ReferenceEquals(candidate, parent)))
            {
                candidateIndex++;
                continue;
            }

            candidateIndex++;
            return candidate;
        }

        return null;
    }

    private static void AddFamilyCandidate(DatRecordEntry record, List<DatRecordEntry> familyCandidates)
    {
        if (!record.IsFamilyCandidate)
        {
            record.IsFamilyCandidate = true;
            familyCandidates.Add(record);
        }
    }

    private static int EnsureColumn(
        int columnIndex,
        Func<IReadOnlyList<string>, int> findColumn,
        string columnName,
        List<string> headers,
        ActiveDatRecords records)
    {
        if (columnIndex >= 0)
        {
            return columnIndex;
        }

        columnIndex = findColumn(headers);
        if (columnIndex >= 0)
        {
            return columnIndex;
        }

        headers.Add(columnName);
        foreach (var record in records.OriginalOrder)
        {
            while (record.Fields.Count < headers.Count - 1)
            {
                record.Fields.Add(string.Empty);
            }

            record.Fields.Add(string.Empty);
        }

        return headers.Count - 1;
    }

    private static int EnsureParentIdColumn(int columnIndex, List<string> headers, ActiveDatRecords records) =>
        EnsureColumn(columnIndex, FindParentIdColumn, "PARENTDOCID", headers, records);

    private static int EnsureEndAttachColumn(int columnIndex, List<string> headers, ActiveDatRecords records) =>
        EnsureColumn(columnIndex, FindEndAttachColumn, "ENDATTACH", headers, records);

    private static int FindParentIdColumn(IReadOnlyList<string> headers) =>
        IndexOfAny(headers, "PARENTDOCID", "Parent Doc ID");

    private static int FindEndAttachColumn(IReadOnlyList<string> headers) =>
        IndexOfAny(headers, "ENDATTACH", "END_ATTACH", "END ATTACH");

    private static int IndexOfAny(IReadOnlyList<string> values, params string[] names)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (names.Any(name => string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    private static Dictionary<string, DatRecordEntry> BuildDatRecordIndex(IReadOnlyList<DatRecordEntry> records, bool includeBates)
    {
        var index = new Dictionary<string, DatRecordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (includeBates && !string.IsNullOrEmpty(record.Bates))
            {
                index.TryAdd(record.Bates, record);
            }

            if (!string.IsNullOrEmpty(record.DocId))
            {
                index.TryAdd(record.DocId, record);
            }
        }

        return index;
    }

    private static Dictionary<string, OptDocEntry> BuildOptDocumentIndex(IReadOnlyList<OptDocEntry> documents)
    {
        var index = new Dictionary<string, OptDocEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            index.TryAdd(document.DocKey, document);
        }

        return index;
    }

    private static OptDocEntry? FindMatchingOpt(IReadOnlyDictionary<string, OptDocEntry> documentsByKey, string datKey)
    {
        if (string.IsNullOrEmpty(datKey))
        {
            return null;
        }

        if (documentsByKey.TryGetValue(datKey, out var document))
        {
            return document;
        }

        return documentsByKey.TryGetValue($"{datKey}{FirstPageSuffix}", out document) ? document : null;
    }

    private static DatRecordEntry? FindMatchingDatRecord(
        IReadOnlyDictionary<string, DatRecordEntry> datRecordsByKey,
        string optDocKey)
    {
        if (string.IsNullOrEmpty(optDocKey))
        {
            return null;
        }

        if (datRecordsByKey.TryGetValue(optDocKey, out var datDocument))
        {
            return datDocument;
        }

        return optDocKey.EndsWith(FirstPageSuffix, StringComparison.OrdinalIgnoreCase)
            && optDocKey.Length > FirstPageSuffix.Length
            && datRecordsByKey.TryGetValue(optDocKey[..^FirstPageSuffix.Length], out datDocument)
                ? datDocument
                : null;
    }

    private static OptDocEntry? FindMatchingOptForDat(
        IReadOnlyDictionary<string, OptDocEntry> documentsByKey,
        DatRecordEntry datRecord)
    {
        return FindMatchingOpt(documentsByKey, datRecord.Bates)
            ?? FindMatchingOpt(documentsByKey, datRecord.DocId);
    }

    private static string ReplaceVolumeInPath(string path, string newVolume)
    {
        var sep = path.Contains('/', StringComparison.Ordinal) ? '/' : '\\';
        var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        bool replaced = false;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("VOL", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = newVolume;
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            return $"IMAGES{sep}{newVolume}{sep}{Path.GetFileName(path)}";
        }

        return string.Join(sep.ToString(), parts);
    }
}
