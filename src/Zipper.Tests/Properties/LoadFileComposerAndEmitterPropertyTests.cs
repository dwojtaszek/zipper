using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using FsCheck.Xunit;
using Xunit;
using Zipper.Config;
using Zipper.Emails;
using Zipper.LoadFiles;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Properties;

/// <summary>
/// Property-based tests for Load File Composers, Emitter, EDRM-XML Carve-Out, Family Planning,
/// Companion Audit File serialization, and Coded Value generation.
/// </summary>
[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class LoadFileComposerAndEmitterPropertyTests
{
    static LoadFileComposerAndEmitterPropertyTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Property]
    public void DatStandardComposer_Compose_HeaderColumnCountShouldAlwaysEqualEveryRecordValueCount(
        MetadataFlagCombinationInput input)
    {
        int fileCount = Math.Clamp(input.FileCount, 1, 10);
        int seed = input.Seed;
        var request = BuildRequest(input, fileCount, seed);
        var namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;

        var headerColumns = DatStandardComposer.BuildHeaders(request, namingConvention);
        var batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;
        var processedFiles = BuildProcessedFiles(request, fileCount, input.AttachmentRate);

        // 1. Direct DatStandardComposer verification
        var standardComposer = new DatStandardComposer(request, batesSequence, headerColumns);
        var composedStandard = standardComposer.ComposeStandard(processedFiles).ToList();

        Assert.NotEmpty(composedStandard);
        foreach (var record in composedStandard)
        {
            Assert.Equal(headerColumns.Count, record.Values.Count);
            Assert.Equal(headerColumns.Count, record.Columns.Count);
            Assert.Equal(record.Columns.Count, record.Values.Count);
        }

        // 2. Multi-mode DatComposer and DatComposingWriter branch verification
        var modes = new[] { WriterMode.Standard, WriterMode.LoadfileOnly, WriterMode.ProductionSet };
        foreach (var mode in modes)
        {
            var composer = new DatComposer(request, mode);
            Assert.NotEmpty(composer.HeaderColumns);

            var records = composer.Compose(processedFiles).ToList();
            Assert.NotEmpty(records);
            foreach (var record in records)
            {
                Assert.Equal(composer.HeaderColumns.Count, record.Values.Count);
                Assert.Equal(composer.HeaderColumns.Count, record.Columns.Count);
            }

            var writer = new DatComposingWriter(mode);
            Assert.Equal(".dat", writer.FileExtension);
            Assert.False(string.IsNullOrEmpty(writer.FormatName));

            using var ms = new MemoryStream();
            writer.WriteAsync(ms, request, processedFiles).GetAwaiter().GetResult();
            Assert.True(ms.Length > 0);
        }

        // 3. Factory instantiations across modes
        var factoryWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat, WriterMode.Standard);
        Assert.IsType<DatComposingWriter>(factoryWriter);
    }

    [Property]
    public void StandardRowComposer_Compose_ShouldBeDeterministicForIdenticalSeedAndFileData(
        MetadataFlagCombinationInput input)
    {
        int fileCount = Math.Clamp(input.FileCount, 1, 8);
        int seed = input.Seed;
        var request = BuildRequest(input, fileCount, seed);
        var processedFiles = BuildProcessedFiles(request, fileCount, input.AttachmentRate);

        // Verify CSV composer determinism
        var csv1 = new CsvComposer(request);
        var csv2 = new CsvComposer(request);
        Assert.Equal(csv1.HeaderColumns, csv2.HeaderColumns);

        var csvRecords1 = csv1.Compose(processedFiles).ToList();
        var csvRecords2 = csv2.Compose(processedFiles).ToList();
        AssertComposedRecordsEqual(csv1.HeaderColumns, csvRecords1, csvRecords2);

        // Verify Concordance composer determinism
        var conc1 = new ConcordanceComposer(request);
        var conc2 = new ConcordanceComposer(request);
        Assert.Equal(conc1.HeaderColumns, conc2.HeaderColumns);

        var concRecords1 = conc1.Compose(processedFiles).ToList();
        var concRecords2 = conc2.Compose(processedFiles).ToList();
        AssertComposedRecordsEqual(conc1.HeaderColumns, concRecords1, concRecords2);

        // Verify SyntheticRowValues determinism
        var now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var wi = processedFiles[0].WorkItem;
        var fd = processedFiles[0];
        var meta1 = SyntheticRowValues.Metadata(wi, fd, new Random(seed), now);
        var meta2 = SyntheticRowValues.Metadata(wi, fd, new Random(seed), now);
        Assert.Equal(meta1, meta2);

        var eml1 = SyntheticRowValues.Eml(wi, fd, new Random(seed), now);
        var eml2 = SyntheticRowValues.Eml(wi, fd, new Random(seed), now);
        Assert.Equal(eml1, eml2);

        var colMeta1 = SyntheticRowValues.CollectionMetadata(wi, new Random(seed), now);
        var colMeta2 = SyntheticRowValues.CollectionMetadata(wi, new Random(seed), now);
        Assert.Equal(colMeta1, colMeta2);

        Assert.Equal(request.Output.CompressionMethod.ToString(), SyntheticRowValues.CompressionMethod(request));
    }

    [Property]
    public void LoadFileEmitter_Emit_ByteCountAndLineCountShouldMatchPreambleAndEolContract(
        EmitterPropertyInput input)
    {
        Encoding encoding = input.EncodingName switch
        {
            "UTF-16LE" => new UnicodeEncoding(bigEndian: false, byteOrderMark: input.UseByteOrderMark),
            "ANSI" => CodePagesEncodingProvider.Instance.GetEncoding(1252)!,
            "ASCII" => Encoding.ASCII,
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: input.UseByteOrderMark),
        };

        string eol = LoadFileEmitter.GetEolString(input.EndOfLine);
        int recordCount = Math.Clamp(input.RecordCount, 0, 8);
        int colCount = Math.Clamp(input.ColumnCount, 1, 5);

        var headerColumns = input.HasHeader
            ? Enumerable.Range(1, colCount).Select(c => $"COL_{c.ToString(CultureInfo.InvariantCulture)}").ToList()
            : [];

        var records = new List<LoadFileRecord>(recordCount);
        for (int r = 1; r <= recordCount; r++)
        {
            var values = Enumerable.Range(1, colCount)
                .Select(c => $"V_{r.ToString(CultureInfo.InvariantCulture)}_{c.ToString(CultureInfo.InvariantCulture)}")
                .ToList();
            var recordColumns = input.HasHeader
                ? headerColumns
                : Enumerable.Range(1, colCount).Select(c => $"C_{c.ToString(CultureInfo.InvariantCulture)}").ToList();
            records.Add(LoadFileRecordBuilder.Build(recordColumns, values, $"DOC_{r.ToString(CultureInfo.InvariantCulture)}"));
        }

        var serializer = new CsvSerializer();
        using var stream = new MemoryStream();
        LoadFileEmitter.EmitAsync(
            stream,
            serializer,
            headerColumns,
            records,
            encoding,
            eol,
            chaosEngine: null).GetAwaiter().GetResult();

        byte[] bytes = stream.ToArray();
        byte[] preamble = encoding.GetPreamble();

        // 1. Verify preamble
        if (preamble.Length > 0)
        {
            Assert.True(bytes.Length >= preamble.Length);
            Assert.Equal(preamble, bytes[..preamble.Length]);
        }

        // 2. Verify exact byte count conservation
        int expectedByteCount = preamble.Length;
        if (input.HasHeader)
        {
            expectedByteCount += encoding.GetByteCount(serializer.RenderHeader(headerColumns) + eol);
        }

        foreach (var record in records)
        {
            expectedByteCount += encoding.GetByteCount(serializer.RenderRecord(record) + eol);
        }

        Assert.Equal(expectedByteCount, bytes.Length);

        // 3. Verify line count and line endings
        string decodedText = encoding.GetString(bytes[preamble.Length..]);
        int expectedLineCount = (input.HasHeader ? 1 : 0) + records.Count;

        if (expectedLineCount == 0)
        {
            Assert.Equal(string.Empty, decodedText);
        }
        else
        {
            Assert.EndsWith(eol, decodedText);
            string trimmed = decodedText[..^eol.Length];
            string[] lines = trimmed.Split(eol);
            Assert.Equal(expectedLineCount, lines.Length);
        }
    }

    [Property]
    public void XmlLoadFileWriter_Write_ShouldProduceWellFormedEdrmXmlWithEscapedControlAndUnicodeCharacters(
        XmlLoadFilePropertyInput input)
    {
        int fileCount = Math.Clamp(input.FileCount, 1, 6);
        int seed = input.Seed;

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "eml",
                FileCount = fileCount,
                WithText = input.WithText,
            },
            Metadata = new MetadataConfig
            {
                WithMetadata = true,
                WithFamilies = input.WithFamilies,
                Seed = seed,
            },
            LoadFile = new LoadFileConfig
            {
                AttachmentRate = input.AttachmentRate,
                Formats = [LoadFileFormat.EdrmXml],
            },
            Hash = new HashConfig
            {
                Mode = input.WithHash ? HashMode.Simulated : HashMode.None,
                Algorithms = input.WithHash
                    ? new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5, Config.HashAlgorithm.SHA256 }
                    : new HashSet<Config.HashAlgorithm>(),
            },
        };

        var processedFiles = new List<FileData>(fileCount);
        for (int i = 1; i <= fileCount; i++)
        {
            bool hasAttachment = input.WithFamilies && (input.AttachmentRate > 0) && (i % 2 == 1);
            processedFiles.Add(new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = i,
                    FolderNumber = 1,
                    FolderName = "001",
                    FileName = $"DOC{i:D8}.eml",
                    FilePathInZip = $"NATIVES/001/DOC{i:D8}.eml",
                },
                DataLength = 2048 * i,
                PageCount = 1,
                Attachment = hasAttachment ? ($"att_{i}.pdf", new byte[] { 65, 66, 67 }) : null,
                AttachmentLength = hasAttachment ? 512 : 0,
                Email = new Email
                {
                    Subject = $"Subject {i}: {input.MetadataValue}",
                    From = $"from{i}@example.com",
                    To = $"to{i}@example.com",
                    Cc = $"cc{i}@example.com",
                    SentDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i),
                },
            });
        }

        var writer = new XmlLoadFileWriter();
        Assert.Equal(".xml", writer.FileExtension);
        Assert.Equal("XML", writer.FormatName);

        using var ms = new MemoryStream();
        writer.WriteAsync(ms, request, processedFiles).GetAwaiter().GetResult();

        ms.Position = 0;
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            Async = true,
        };

        using var xmlReader = XmlReader.Create(ms, readerSettings);
        var doc = XDocument.Load(xmlReader);

        Assert.NotNull(doc.Root);
        Assert.Equal("Root", doc.Root.Name.LocalName);
        Assert.Equal("Export", doc.Root.Attribute("DataInterchangeType")?.Value);

        var batch = doc.Root.Element("Batch");
        Assert.NotNull(batch);

        int childCount = processedFiles.Count(f => input.WithFamilies && f.Attachment.HasValue);
        int expectedDocCount = processedFiles.Count + childCount;
        var documents = batch.Elements("Document").ToList();
        Assert.Equal(expectedDocCount, documents.Count);

        var files = doc.Descendants("File").ToList();
        int expectedFilesPerDoc = input.WithText ? 2 : 1;
        Assert.Equal(expectedDocCount * expectedFilesPerDoc, files.Count);

        var relationships = batch.Element("Relationships");
        if (childCount > 0)
        {
            Assert.NotNull(relationships);
            var relationshipElements = relationships.Elements("Relationship").ToList();
            Assert.Equal(childCount, relationshipElements.Count);
            foreach (var rel in relationshipElements)
            {
                Assert.Equal("Attachment", rel.Attribute("Type")?.Value);
                Assert.False(string.IsNullOrEmpty(rel.Attribute("ParentDocID")?.Value));
                Assert.False(string.IsNullOrEmpty(rel.Attribute("ChildDocID")?.Value));
            }
        }

        var parentDocs = batch.Elements("Document")
            .Where(d => !d.Attribute("DocID")!.Value.Contains("_A", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(processedFiles.Count, parentDocs.Count);
        foreach (var parentDoc in parentDocs)
        {
            var subjectTag = parentDoc.Descendants("Tag")
                .FirstOrDefault(t => t.Attribute("TagName")?.Value == "Subject");
            Assert.NotNull(subjectTag);
            var val = subjectTag.Attribute("TagValue")?.Value;
            Assert.NotNull(val);
            if (!string.IsNullOrEmpty(input.MetadataValue))
            {
                Assert.Contains(input.MetadataValue, val);
            }
        }

        var childDocs = batch.Elements("Document")
            .Where(d => d.Attribute("DocID")!.Value.Contains("_A", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(childCount, childDocs.Count);
        foreach (var childDoc in childDocs)
        {
            var subjectTag = childDoc.Descendants("Tag")
                .FirstOrDefault(t => t.Attribute("TagName")?.Value == "Subject");
            if (subjectTag is not null)
            {
                Assert.Equal(string.Empty, subjectTag.Attribute("TagValue")?.Value);
            }
        }
    }

    [Property]
    public void FamilyPlan_Create_ShouldPreserveParentOrderAndAttachmentBoundsAndDeterminism(
        FamilyPlanPropertyInput input)
    {
        int fileCount = Math.Clamp(input.FileCount, 1, 20);
        double rate = Math.Clamp(input.AttachmentRate, 0.0, 100.0);
        int seed = input.Seed;

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "eml",
                FileCount = fileCount,
            },
            Metadata = new MetadataConfig
            {
                Seed = seed,
                WithFamilies = true,
            },
            LoadFile = new LoadFileConfig
            {
                AttachmentRate = rate,
            },
        };

        // 1. AttachmentRate == 0 contract produces zero attachments
        var zeroRateRequest = new FileGenerationRequest
        {
            Output = request.Output,
            Metadata = request.Metadata,
            LoadFile = request.LoadFile with { AttachmentRate = 0.0 },
        };
        Assert.Equal(0, FamilyPlan.GetSimulatedChildCount(zeroRateRequest));
        for (int i = 1; i <= fileCount; i++)
        {
            Assert.False(FamilyPlan.HasAttachment(zeroRateRequest, i));
        }

        // 2. Bounds and determinism
        long childCount1 = FamilyPlan.GetSimulatedChildCount(request);
        long childCount2 = FamilyPlan.GetSimulatedChildCount(request);
        Assert.Equal(childCount1, childCount2);
        Assert.InRange(childCount1, 0, fileCount);

        var decisions1 = Enumerable.Range(1, fileCount).Select(i => FamilyPlan.HasAttachment(request, i)).ToList();
        var decisions2 = Enumerable.Range(1, fileCount).Select(i => FamilyPlan.HasAttachment(request, i)).ToList();
        Assert.Equal(decisions1, decisions2);
        Assert.Equal(childCount1, decisions1.Count(d => d));

        // 3. Parent order: parent index < child attachment index in document ordering
        for (int i = 1; i <= fileCount; i++)
        {
            if (decisions1[i - 1])
            {
                var fileData = new FileData
                {
                    WorkItem = new FileWorkItem { Index = i, FileName = $"DOC{i:D8}.eml" },
                    Attachment = ($"att_{i}.pdf", new byte[] { 1 }),
                };
                var (parentId, childId, hasAttachment) = DatComposerShared.GetFamilyIdentifiers(fileData, request, batesSequence: null);
                Assert.True(hasAttachment);
                Assert.True(string.CompareOrdinal(parentId, childId) < 0);
                Assert.Equal($"{parentId}_A001", childId);
            }
        }

        // 4. Filename sanitization idempotence
        var sanitized = FamilyPlan.SanitizeAttachmentFilename(@"dir/subdir\sample.pdf");
        Assert.Equal("sample.pdf", sanitized);
        Assert.Equal(sanitized, FamilyPlan.SanitizeAttachmentFilename(sanitized));
        Assert.Equal(string.Empty, FamilyPlan.SanitizeAttachmentFilename(string.Empty));
    }

    [Property]
    public void LoadFileAuditWriter_Write_ShouldRoundTripJsonAndConserveRecordTotals(
        AuditWriterPropertyInput input)
    {
        int fileCount = Math.Clamp(input.FileCount, 1, 15);
        int rate = Math.Clamp(input.AttachmentRate, 0, 100);
        int seed = input.Seed;
        var format = input.Format;

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "eml",
                FileCount = fileCount,
            },
            Metadata = new MetadataConfig
            {
                Seed = seed,
                WithFamilies = input.WithFamilies,
            },
            LoadFile = new LoadFileConfig
            {
                AttachmentRate = rate,
                Formats = [format],
            },
        };

        IReadOnlyList<FileData> composedRecords = input.IsLoadfileOnly
            ? []
            : Enumerable.Range(1, fileCount).Select(i => new FileData
            {
                WorkItem = new FileWorkItem { Index = i, FileName = $"DOC{i:D8}.eml" },
                DataLength = 1000,
                PageCount = 1,
                Attachment = input.WithFamilies && (i % 2 == 1) ? ($"att_{i}.pdf", new byte[] { 1 }) : null,
            }).ToList();

        string outputPath = $"/virtual/export.{format.ToString().ToLowerInvariant()}";
        string json = LoadFileAuditWriter.GenerateAuditJson(outputPath, request, composedRecords, anomalies: null, format: format);
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("fileName", out var fn) && !string.IsNullOrEmpty(fn.GetString()));
        Assert.True(root.TryGetProperty("format", out var fmtProp) && !string.IsNullOrEmpty(fmtProp.GetString()));
        Assert.True(root.TryGetProperty("totalRecords", out var tr));

        long totalRecordsJson = tr.GetInt64();
        var (expectedRecords, _) = LoadFileAuditWriter.ComputeRecordCounts(request, composedRecords, format);
        Assert.Equal(expectedRecords, totalRecordsJson);

        if (format != LoadFileFormat.Opt)
        {
            long totalFiles = composedRecords.Count > 0 ? composedRecords.Count : request.Output.FileCount;
            long totalAttachments = composedRecords.Count > 0
                ? composedRecords.Count(f => input.WithFamilies && f.Attachment.HasValue)
                : (input.WithFamilies && rate > 0 ? FamilyPlan.GetSimulatedChildCount(request) : 0);
            Assert.Equal(totalFiles + totalAttachments, totalRecordsJson);
        }

        Assert.True(root.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("encoding", out _));
        Assert.True(props.TryGetProperty("lineEnding", out _));
        Assert.True(props.TryGetProperty("delimiters", out var delims));
        Assert.True(delims.TryGetProperty("column", out _));
        Assert.True(delims.TryGetProperty("quote", out _));
        Assert.True(delims.TryGetProperty("newline", out _));
        Assert.True(delims.TryGetProperty("multiValue", out _));
        Assert.True(delims.TryGetProperty("nestedValue", out _));

        Assert.True(root.TryGetProperty("chaosMode", out var chaos));
        Assert.True(chaos.TryGetProperty("enabled", out _));
        Assert.True(chaos.TryGetProperty("totalAnomalies", out _));
    }

    [Property]
    public void CodedGenerator_Generate_ShouldBeDeterministicAndMemberOfConfiguredCategories(
        CodedGeneratorPropertyInput input)
    {
        var col = new ColumnDefinition
        {
            Name = "CategoryCol",
            Type = "coded",
            MultiValue = input.MultiValue,
            MultiValueCount = new RangeConfig
            {
                Min = Math.Min(input.MultiValueMin, input.MultiValueMax),
                Max = Math.Max(input.MultiValueMin, input.MultiValueMax),
            },
        };

        var settings = new ProfileSettings { MultiValueDelimiter = ";" };
        var generator = new CodedGenerator(input.Categories, input.DistributionIndices, col, settings);

        int safeSeed = input.Seed;
        var ctx1 = new ColumnGenerationContext
        {
            NativeFileIndex = input.DocumentIndex + 1,
            FolderNumber = 1,
            DocumentIndex = input.DocumentIndex,
            Seeded = new Random(safeSeed),
            Now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var ctx2 = new ColumnGenerationContext
        {
            NativeFileIndex = input.DocumentIndex + 1,
            FolderNumber = 1,
            DocumentIndex = input.DocumentIndex,
            Seeded = new Random(safeSeed),
            Now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        string result1 = generator.Generate(ctx1);
        string result2 = generator.Generate(ctx2);

        // 1. Deterministic replay
        Assert.Equal(result1, result2);

        // 2. Member of configured categories
        var parts = result1.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (input.Categories.Length == 0)
        {
            Assert.Empty(parts);
        }
        else
        {
            foreach (var part in parts)
            {
                Assert.Contains(part, input.Categories);
            }

            if (input.MultiValue)
            {
                Assert.Equal(parts.Length, parts.Distinct(StringComparer.Ordinal).Count());
                int distinctPoolCount = input.Categories.Distinct(StringComparer.Ordinal).Count();
                Assert.InRange(parts.Length, 1, Math.Min(col.MultiValueCount.Max, distinctPoolCount));
            }
            else
            {
                Assert.Single(parts);
            }
        }
    }

    private static FileGenerationRequest BuildRequest(
        MetadataFlagCombinationInput input,
        int fileCount,
        int seed)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = fileCount,
                FileType = input.IncludeEmailFields ? "eml" : (input.IncludePageCount ? "tiff" : "pdf"),
                WithText = input.IncludeTextPath,
                CompressionMethod = input.IncludeCompressionMethod ? ZipCompressionMethod.Deflate : ZipCompressionMethod.Store,
            },
            Metadata = new MetadataConfig
            {
                WithMetadata = input.IncludeMetadata,
                WithFamilies = input.IncludeEmailFields,
                WithCollectionMetadata = input.IncludeDateRange,
                ColumnProfile = input.ColumnProfile is not null ? BuiltInProfiles.GetProfile(input.ColumnProfile) : null,
                Seed = seed,
            },
            LoadFile = new LoadFileConfig
            {
                AttachmentRate = input.AttachmentRate,
                Formats = [LoadFileFormat.Dat],
            },
            Hash = new HashConfig
            {
                Mode = input.IncludeHash ? HashMode.Simulated : HashMode.None,
                Algorithms = input.IncludeHash
                    ? new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5, Config.HashAlgorithm.SHA256 }
                    : new HashSet<Config.HashAlgorithm>(),
            },
            Tiff = new TiffConfig
            {
                PageRange = input.IncludePageCount ? (1, 5) : null,
            },
            Bates = input.BatesConfig,
            Production = input.ProductionConfig ?? new ProductionConfig(),
        };
    }

    private static List<FileData> BuildProcessedFiles(
        FileGenerationRequest request,
        int fileCount,
        int attachmentRate)
    {
        var files = new List<FileData>(fileCount);
        for (int i = 1; i <= fileCount; i++)
        {
            int folderNum = ((i - 1) % 50) + 1;
            string ext = request.Output.FileTypeLower;
            var wi = new FileWorkItem
            {
                Index = i,
                FolderNumber = folderNum,
                FolderName = $"{folderNum:D3}",
                FileName = $"DOC{i:D8}.{ext}",
                FilePathInZip = $"NATIVES/{folderNum:D3}/DOC{i:D8}.{ext}",
            };

            bool hasAttachment = request.Metadata.WithFamilies
                && request.Output.IsEml
                && (attachmentRate > 0)
                && (i % 2 == 1);

            files.Add(new FileData
            {
                WorkItem = wi,
                DataLength = 1024 * i,
                PageCount = request.Tiff.PageRange.HasValue ? ((i % 5) + 1) : 1,
                Attachment = hasAttachment ? ($"att_{i}.pdf", new byte[] { 1, 2, 3 }) : null,
                AttachmentLength = hasAttachment ? 512 : 0,
                Email = request.Output.IsEml ? new Email
                {
                    To = $"recipient{i}@example.com",
                    From = $"sender{i}@example.com",
                    Cc = $"cc{i}@example.com",
                    Subject = $"Subject {i}",
                    SentDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i),
                } : null,
            });
        }

        return files;
    }

    private static void AssertComposedRecordsEqual(
        IReadOnlyList<string> headerColumns,
        IReadOnlyList<LoadFileRecord> expected,
        IReadOnlyList<LoadFileRecord> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].RecordId, actual[i].RecordId);
            Assert.Equal(expected[i].Columns, actual[i].Columns);
            Assert.Equal(expected[i].Values, actual[i].Values);
            Assert.Equal(headerColumns.Count, actual[i].Values.Count);
        }
    }
}
