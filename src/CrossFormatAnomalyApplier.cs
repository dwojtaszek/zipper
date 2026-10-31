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
/// Coordinates anomalies that span multiple load files in a Production Set (DAT and OPT),
/// breaking cross-file invariants such as page count consistency, document sort order,
/// volume-relative image path alignment, and family relationships (orphaned attachments,
/// missing children, broken parent pointers).
/// </summary>
internal static class CrossFormatAnomalyApplier
{
    private sealed record DatRecordEntry(
        long LineNumber,
        List<string> Fields,
        string DocId,
        string Bates,
        string Volume,
        string ImagePath,
        string PageCountRaw,
        string ParentId,
        string BegAttach,
        string EndAttach)
    {
        public string OriginalLine { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether this record's fields were mutated and must be re-rendered.
        /// </summary>
        public bool IsDirty { get; set; }
    }

    private sealed class OptDocEntry
    {
        public required string DocKey { get; init; }
        public required long BreakLineNumber { get; set; }
        public required string DeclaredPageCountRaw { get; set; }
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
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
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
        int volumeIdx = headers.FindIndex(h => string.Equals(h, "VOLUME", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "Volume", StringComparison.OrdinalIgnoreCase));
        int imageIdx = headers.FindIndex(h => string.Equals(h, "IMAGE_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "IMAGE", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "Image Path", StringComparison.OrdinalIgnoreCase));
        int pageCountIdx = headers.FindIndex(h => string.Equals(h, "PAGECOUNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGE_COUNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGE COUNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGECNT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "PAGE_CNT", StringComparison.OrdinalIgnoreCase));
        int parentIdIdx = headers.FindIndex(h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "Parent Doc ID", StringComparison.OrdinalIgnoreCase));
        int begAttachIdx = headers.FindIndex(h => string.Equals(h, "BEGATTACH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "BEG_ATTACH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "BEG ATTACH", StringComparison.OrdinalIgnoreCase));
        int endAttachIdx = headers.FindIndex(h => string.Equals(h, "ENDATTACH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "END_ATTACH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "END ATTACH", StringComparison.OrdinalIgnoreCase));

        var datRecords = new List<DatRecordEntry>(datLines.Count - 1);
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
            string vol = volumeIdx >= 0 && volumeIdx < fields.Count ? fields[volumeIdx] : string.Empty;
            string img = imageIdx >= 0 && imageIdx < fields.Count ? fields[imageIdx] : string.Empty;
            string pg = pageCountIdx >= 0 && pageCountIdx < fields.Count ? fields[pageCountIdx] : string.Empty;
            string parent = parentIdIdx >= 0 && parentIdIdx < fields.Count ? fields[parentIdIdx] : string.Empty;
            string beg = begAttachIdx >= 0 && begAttachIdx < fields.Count ? fields[begAttachIdx] : string.Empty;
            string end = endAttachIdx >= 0 && endAttachIdx < fields.Count ? fields[endAttachIdx] : string.Empty;

            if (string.IsNullOrEmpty(docId) && !string.IsNullOrEmpty(bates))
            {
                docId = bates;
            }

            datRecords.Add(new DatRecordEntry(i + 1, fields, docId, bates, vol, img, pg, parent, beg, end) { OriginalLine = line });
        }

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
            string pageCount = cols.Length > 6 ? cols[6].Trim() : string.Empty;

            if (isBreak || currentOptDoc is null)
            {
                currentOptDoc = new OptDocEntry
                {
                    DocKey = key,
                    BreakLineNumber = j + 1,
                    DeclaredPageCountRaw = pageCount,
                    Volume = vol,
                    FirstImagePath = img,
                };
                optDocs.Add(currentOptDoc);
            }

            currentOptDoc.PageRows.Add((j + 1, line, cols));
        }

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

        int typeIndex = 0;
        for (int k = 0; k < targetCount; k++)
        {
            string chosenType = candidateTypes[typeIndex % candidateTypes.Count];
            typeIndex++;

            switch (chosenType)
            {
                case "desync-pagecount":
                    ApplyDesyncPagecount(datRecords, optDocs, random, datAnomalies, optAnomalies, datFileName, optFileName, pageCountIdx >= 0 ? headers[pageCountIdx] : "PAGECOUNT");
                    break;
                case "desync-order":
                    ApplyDesyncOrder(datRecords, optDocs, random, datAnomalies, optAnomalies, datFileName, optFileName, batesIdx >= 0 ? headers[batesIdx] : (docIdIdx >= 0 ? headers[docIdIdx] : "DOCID"));
                    break;
                case "desync-path":
                    ApplyDesyncPath(datRecords, optDocs, random, datAnomalies, optAnomalies, datFileName, optFileName, imageIdx >= 0 ? headers[imageIdx] : "IMAGE_PATH");
                    break;
                case "orphan-attachment":
                    ApplyOrphanAttachment(datRecords, optDocs, random, datAnomalies, optAnomalies, datFileName, optFileName, parentIdIdx >= 0 ? headers[parentIdIdx] : "PARENTDOCID", headers, colDelimChar, quoteDelimChar);
                    break;
                case "missing-child":
                    ApplyMissingChild(datRecords, optDocs, random, datAnomalies, optAnomalies, datFileName, optFileName, endAttachIdx >= 0 ? headers[endAttachIdx] : "ENDATTACH", headers, colDelimChar, quoteDelimChar);
                    break;
                case "broken-parent":
                    ApplyBrokenParent(datRecords, optDocs, random, datAnomalies, optAnomalies, datFileName, optFileName, parentIdIdx >= 0 ? headers[parentIdIdx] : "PARENTDOCID", parentIdIdx, headers);
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
        foreach (var rec in datRecords)
        {
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

        return new CrossFormatAnomalyResult
        {
            DatLines = outDatLines,
            OptLines = outOptLines,
            DatAnomalies = datAnomalies,
            OptAnomalies = optAnomalies,
        };
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

    private static void ApplyDesyncPagecount(
        List<DatRecordEntry> datRecords,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string pageCountColumnName)
    {
        if (optDocs.Count == 0 || datRecords.Count == 0)
        {
            return;
        }

        // Find a document with multi-page OPT rows, or pick any document
        int targetDocIdx = optDocs.FindIndex(d => d.PageRows.Count > 1);
        if (targetDocIdx < 0)
        {
            targetDocIdx = random.Next(optDocs.Count);
        }

        var optDoc = optDocs[targetDocIdx];
        var datDoc = FindMatchingDat(datRecords, optDoc.DocKey) ?? datRecords[Math.Min(targetDocIdx, datRecords.Count - 1)];

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
            string baseKey = optDoc.DocKey.EndsWith("_001", StringComparison.OrdinalIgnoreCase)
                ? optDoc.DocKey[..^4]
                : optDoc.DocKey;
            string extraKey = $"{baseKey}_002";
            string firstImg = optDoc.FirstImagePath;
            string extraImg = firstImg.Replace("_001", "_002");
            if (string.Equals(extraImg, firstImg, StringComparison.Ordinal))
            {
                var ext = Path.GetExtension(firstImg);
                extraImg = $"{firstImg[..^ext.Length]}_002{ext}";
            }

            string extraLine = $"{extraKey},{optDoc.Volume},{extraImg},,,,,";
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
    }

    private static void ApplyDesyncOrder(
        List<DatRecordEntry> datRecords,
        List<OptDocEntry> optDocs,
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

        int idx1 = random.Next(optDocs.Count - 1);
        int idx2 = idx1 + 1;

        var optDoc1 = optDocs[idx1];
        var optDoc2 = optDocs[idx2];

        // Swap documents in OPT
        optDocs[idx1] = optDoc2;
        optDocs[idx2] = optDoc1;

        var datDoc1 = FindMatchingDat(datRecords, optDoc1.DocKey) ?? datRecords[idx1];
        var datDoc2 = FindMatchingDat(datRecords, optDoc2.DocKey) ?? datRecords[idx2];

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
                LineNumber = optDoc2.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = optDoc2.DocKey,
                Column = "DocBreak",
            },
        });

        optAnomalies.Add(new ChaosAnomaly
        {
            File = optFileName,
            LineNumber = optDoc2.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
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
    }

    private static void ApplyDesyncPath(
        List<DatRecordEntry> datRecords,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string imageColumnName)
    {
        if (optDocs.Count == 0 || datRecords.Count == 0)
        {
            return;
        }

        int targetIdx = random.Next(optDocs.Count);
        var optDoc = optDocs[targetIdx];
        var datDoc = FindMatchingDat(datRecords, optDoc.DocKey) ?? datRecords[Math.Min(targetIdx, datRecords.Count - 1)];

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
    }

    private static void ApplyOrphanAttachment(
        List<DatRecordEntry> datRecords,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string parentIdColumnName,
        List<string> headers,
        char colDelimChar,
        char quoteDelimChar)
    {
        // Try finding a child record with a parent
        if (datRecords.Count == 0 || optDocs.Count == 0)
        {
            return;
        }

        int childIdx = datRecords.FindIndex(r => !string.IsNullOrEmpty(r.ParentId));
        if (childIdx >= 0)
        {
            var childRec = datRecords[childIdx];
            var parentRec = datRecords.FirstOrDefault(r => string.Equals(r.DocId, childRec.ParentId, StringComparison.OrdinalIgnoreCase));
            if (parentRec is not null)
            {
                // Remove parent from DAT (leaving child orphaned in DAT)
                datRecords.Remove(parentRec);

                var optParent = optDocs.FirstOrDefault(o => string.Equals(o.DocKey, parentRec.DocId, StringComparison.OrdinalIgnoreCase)) ?? optDocs[0];

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
        }

        // If no existing family, turn a DAT record into an orphaned child
        int targetIdx = Math.Min(1, datRecords.Count - 1);
        var targetRec = datRecords[targetIdx];
        string missingParentId = "DOC_PARENT_MISSING";
        string orphanedChildId = $"{missingParentId}_A001";

        int pIdx = headers.FindIndex(h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase));
        if (pIdx < 0)
        {
            headers.Add("PARENTDOCID");
            pIdx = headers.Count - 1;
            foreach (var r in datRecords)
            {
                r.Fields.Add(string.Empty);
            }
        }

        targetRec.Fields[pIdx] = missingParentId;
        int dIdx = headers.FindIndex(h => string.Equals(h, "DOCID", StringComparison.OrdinalIgnoreCase));
        if (dIdx >= 0)
        {
            targetRec.Fields[dIdx] = orphanedChildId;
        }

        targetRec.IsDirty = true;

        var optPeer = optDocs[Math.Min(targetIdx, optDocs.Count - 1)];

        datAnomalies.Add(new ChaosAnomaly
        {
            File = datFileName,
            LineNumber = targetRec.LineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = orphanedChildId,
            Column = parentIdColumnName,
            ErrorType = "orphan-attachment",
            Description = $"Attachment row '{orphanedChildId}' references non-existent parent '{missingParentId}'.",
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
            Description = $"Orphaned family anomaly coordinated with DAT record '{orphanedChildId}'.",
            Peer = new ChaosAnomalyPeer
            {
                File = datFileName,
                LineNumber = targetRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = orphanedChildId,
                Column = parentIdColumnName,
            },
        });
    }

    private static void ApplyMissingChild(
        List<DatRecordEntry> datRecords,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string endAttachColumnName,
        List<string> headers,
        char colDelimChar,
        char quoteDelimChar)
    {
        // Try finding a child record in DAT to remove from DAT while keeping it in OPT
        if (datRecords.Count == 0 || optDocs.Count == 0)
        {
            return;
        }

        int childIdx = datRecords.FindIndex(r => !string.IsNullOrEmpty(r.ParentId));
        if (childIdx >= 0)
        {
            var childRec = datRecords[childIdx];
            datRecords.RemoveAt(childIdx);

            var optChild = optDocs.FirstOrDefault(o => string.Equals(o.DocKey, childRec.DocId, StringComparison.OrdinalIgnoreCase)) ?? optDocs[Math.Min(childIdx, optDocs.Count - 1)];

            datAnomalies.Add(new ChaosAnomaly
            {
                File = datFileName,
                LineNumber = childRec.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = childRec.ParentId,
                Column = endAttachColumnName,
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
                    RecordID = childRec.ParentId,
                    Column = endAttachColumnName,
                },
            });
            return;
        }

        // If no family exists, remove child from OPT if OPT has multiple docs
        if (optDocs.Count > 1)
        {
            var removedOptDoc = optDocs[^1];
            optDocs.RemoveAt(optDocs.Count - 1);
            var datPeer = datRecords[^1];

            datAnomalies.Add(new ChaosAnomaly
            {
                File = datFileName,
                LineNumber = datPeer.LineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = datPeer.DocId,
                Column = endAttachColumnName,
                ErrorType = "missing-child",
                Description = $"Document '{datPeer.DocId}' exists in DAT but child/document was removed from OPT.",
                Peer = new ChaosAnomalyPeer
                {
                    File = optFileName,
                    LineNumber = removedOptDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = removedOptDoc.DocKey,
                    Column = "DocBreak",
                },
            });

            optAnomalies.Add(new ChaosAnomaly
            {
                File = optFileName,
                LineNumber = removedOptDoc.BreakLineNumber.ToString(CultureInfo.InvariantCulture),
                RecordID = removedOptDoc.DocKey,
                Column = "DocBreak",
                ErrorType = "missing-child",
                Description = $"Removed document '{removedOptDoc.DocKey}' from OPT, leaving orphan in DAT.",
                Peer = new ChaosAnomalyPeer
                {
                    File = datFileName,
                    LineNumber = datPeer.LineNumber.ToString(CultureInfo.InvariantCulture),
                    RecordID = datPeer.DocId,
                    Column = endAttachColumnName,
                },
            });
        }
    }

    private static void ApplyBrokenParent(
        List<DatRecordEntry> datRecords,
        List<OptDocEntry> optDocs,
        Random random,
        List<ChaosAnomaly> datAnomalies,
        List<ChaosAnomaly> optAnomalies,
        string datFileName,
        string optFileName,
        string parentIdColumnName,
        int parentIdIdx,
        List<string> headers)
    {
        // Find a child record with ParentId or any record
        if (datRecords.Count == 0 || optDocs.Count == 0)
        {
            return;
        }

        int targetIdx = datRecords.FindIndex(r => !string.IsNullOrEmpty(r.ParentId));
        if (targetIdx < 0)
        {
            targetIdx = Math.Min(1, datRecords.Count - 1);
        }

        var targetRec = datRecords[targetIdx];
        string corruptParent = "INVALID_PARENT_PTR";

        if (parentIdIdx < 0)
        {
            headers.Add("PARENTDOCID");
            parentIdIdx = headers.Count - 1;
            foreach (var record in datRecords)
            {
                record.Fields.Add(string.Empty);
            }
        }

        if (parentIdIdx < targetRec.Fields.Count)
        {
            targetRec.Fields[parentIdIdx] = corruptParent;
            targetRec.IsDirty = true;
        }

        var optPeer = FindMatchingOpt(optDocs, targetRec.DocId) ?? optDocs[Math.Min(targetIdx, optDocs.Count - 1)];

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
    }

    private static DatRecordEntry? FindMatchingDat(List<DatRecordEntry> records, string optDocKey)
    {
        foreach (var r in records)
        {
            if (!string.IsNullOrEmpty(r.Bates) && string.Equals(r.Bates, optDocKey, StringComparison.OrdinalIgnoreCase))
            {
                return r;
            }

            if (string.Equals(r.DocId, optDocKey, StringComparison.OrdinalIgnoreCase))
            {
                return r;
            }
        }

        return null;
    }

    private static OptDocEntry? FindMatchingOpt(List<OptDocEntry> optDocs, string datKey)
    {
        foreach (var o in optDocs)
        {
            if (string.Equals(o.DocKey, datKey, StringComparison.OrdinalIgnoreCase))
            {
                return o;
            }
        }

        return null;
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
