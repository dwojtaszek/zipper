using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FsCheck.Xunit;
using Xunit;
using Zipper.Cli.Modules;
using Zipper.SourceInput;
using Zipper.Utils;

namespace Zipper.Tests.Properties;

/// <summary>
/// Property-based tests for <see cref="PathValidator"/>, <see cref="SourcePathSanitizer"/>,
/// <see cref="SourceFileTypeMap"/>, <see cref="SourceCsvReader"/>, <see cref="SourceRecordIntake"/>,
/// and <see cref="NamingConventionHelper"/>.
/// </summary>
[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class PathAndSourceInputPropertyTests : IDisposable
{
    private static readonly SearchValues<char> InvalidSourcePathChars = SearchValues.Create("<>|\"?*:");

    private static readonly string[] AsciiReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    private static readonly string[] ProductionIdReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM\u00b9", "COM\u00b2", "COM\u00b3",
        "LPT\u00b9", "LPT\u00b2", "LPT\u00b3"
    ];

    private static readonly (string Extension, string CanonicalType)[] SupportedExtensionMappings =
    [
        (".pdf", "pdf"),
        (".jpg", "jpg"),
        (".jpeg", "jpg"),
        (".tif", "tiff"),
        (".tiff", "tiff"),
        (".eml", "eml"),
        (".docx", "docx"),
        (".xlsx", "xlsx")
    ];

    private static readonly string[] UnsupportedExtensions =
    [
        ".txt", ".exe", ".zip", ".png", ".gif", ".dat", ".opt", ".xml", ".json", ".bin"
    ];

    private static readonly string[] NamingConventions =
    [
        "UPPERCASE",
        "lowercase",
        "PascalCase",
        "snake_case"
    ];

    private static readonly string[] SampleFieldTokens =
    [
        "Doc", "Control", "Number", "Custodian", "Name", "Beg", "Bates",
        "Attachment", "Count", "File", "Path", "Hash", "Value", "Sent", "Date"
    ];

    private readonly string tempRoot;
    private readonly string sharedBaseDir;
    private int iterationCounter;

    public PathAndSourceInputPropertyTests()
    {
        var rawRoot = Path.Combine(Path.GetTempPath(), string.Concat("zipper_pbt_path_", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(rawRoot);
        this.tempRoot = TestPaths.Canonicalize(rawRoot);
        this.sharedBaseDir = Path.Combine(this.tempRoot, "shared_base");
        Directory.CreateDirectory(this.sharedBaseDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.tempRoot))
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup on Windows if an indexer briefly holds a handle.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct CsvRowSeed(
        string RelativePath,
        string DeclaredFileType,
        string CanonicalFileType,
        string? ControlNumber,
        string? BatesNumber,
        string Custodian,
        string Department);

    [Property]
    public void ProductionModule_TryBuild_ShouldRejectUnsafeCharactersAndDeviceNamesInProductionId(
        PathSegmentPropertyInput input,
        byte deviceIndex,
        bool appendExtension)
    {
        var raw = input.RawText ?? string.Empty;

        // 1. Verify ProductionModule Safe Path Segment validation (REQ-170) against our oracle,
        // which covers '/', '\', '..', '.', control chars, Windows reserved chars, trailing space/dot,
        // and DOS device names including superscripts (COM¹, LPT², LPT³).
        if (!raw.Contains(',', StringComparison.Ordinal) && Encoding.UTF8.GetByteCount(raw) <= 250)
        {
            var expectedSafeSegment = IsExpectedSafeProductionIdSegment(raw);
            var actualSafeSegment = TryValidateProductionIdSegment(raw);
            Assert.Equal(expectedSafeSegment, actualSafeSegment);
        }

        // 2. Verify explicit DOS device names (including superscripts) are always rejected as Production IDs,
        // and ASCII DOS device names (with or without extensions/padding) are rejected by SourcePathSanitizer.
        var deviceName = ProductionIdReservedDeviceNames[deviceIndex % ProductionIdReservedDeviceNames.Length];
        var candidateDeviceSegment = appendExtension ? string.Concat(deviceName, ".txt") : deviceName;
        Assert.False(TryValidateProductionIdSegment(candidateDeviceSegment));

        var asciiDevice = AsciiReservedDeviceNames[deviceIndex % AsciiReservedDeviceNames.Length];
        var asciiDeviceWithExt = appendExtension ? string.Concat(asciiDevice, " .pdf") : asciiDevice;
        Assert.False(SourcePathSanitizer.TryNormalize(asciiDeviceWithExt, out _, out var deviceError));
        Assert.False(string.IsNullOrEmpty(deviceError));

        // 3. Verify sanitized alphanumeric/underscore/hyphen segment is always accepted by both validators.
        var safeSegment = BuildSafeSegment(raw, input.ReplacementChar, deviceIndex);
        Assert.True(TryValidateProductionIdSegment(safeSegment));
        Assert.True(SourcePathSanitizer.TryNormalize(safeSegment, out var normalizedSafe, out var safeError));
        Assert.Equal(safeSegment, normalizedSafe);
        Assert.Null(safeError);
    }

    [Property]
    public void PathValidator_IsPathSafe_ShouldPreventTraversalAndResolveUnderBase(
        PathSegmentPropertyInput input,
        byte segmentSeed)
    {
        var baseDir = this.sharedBaseDir;

        // Safe multi-segment child path resolves under baseDir.
        var seg1 = BuildSafeSegment(input.RawText, input.ReplacementChar, segmentSeed);
        var seg2 = BuildSafeSegment(input.RawText, input.ReplacementChar, (byte)(segmentSeed + 1));
        var childPath = Path.Combine(baseDir, seg1, seg2);

        // Null byte at index 0 of the relative segment or path is rejected.
        var leadingNullPath = string.Concat("\0", seg1);
        Assert.Null(ReceiveQuietly(() => PathValidator.ResolveSecurePath(leadingNullPath, baseDir)));
        Assert.False(PathValidator.IsPathSafe(leadingNullPath, baseDir));

        var embeddedNullPath = Path.Combine(baseDir, string.Concat("\0", seg1));
        Assert.Null(ReceiveQuietly(() => PathValidator.ResolveSecurePath(embeddedNullPath, baseDir)));
        Assert.False(PathValidator.IsPathSafe(embeddedNullPath, baseDir));

        var resolvedChild = PathValidator.ResolveSecurePath(childPath, baseDir);
        Assert.NotNull(resolvedChild);
        Assert.True(PathValidator.IsPathSafe(childPath, baseDir));
        Assert.True(PathValidator.IsPathSafe(childPath));
        Assert.NotNull(PathValidator.ResolveSecurePath(childPath));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var baseWithSep = string.Concat(baseDir.TrimEnd(Path.DirectorySeparatorChar), Path.DirectorySeparatorChar);
        var resolvedWithSep = string.Concat(resolvedChild.FullName.TrimEnd(Path.DirectorySeparatorChar), Path.DirectorySeparatorChar);
        Assert.StartsWith(baseWithSep, resolvedWithSep, comparison);

        // Parent traversal escaping baseDir is rejected.
        var traversalEscape = Path.Combine(baseDir, seg1, "..", "..", string.Concat("outside_", seg2));
        Assert.Null(ReceiveQuietly(() => PathValidator.ResolveSecurePath(traversalEscape, baseDir)));
        Assert.False(PathValidator.IsPathSafe(traversalEscape, baseDir));

        // Sibling prefix directory (/tmp/base-sibling vs /tmp/base) is rejected.
        var siblingPrefixPath = string.Concat(baseDir, "-", seg1);
        Assert.Null(ReceiveQuietly(() => PathValidator.ResolveSecurePath(siblingPrefixPath, baseDir)));
        Assert.False(PathValidator.IsPathSafe(siblingPrefixPath, baseDir));
    }

    [Fact]
    public void PathValidator_ResolveSecurePathAndIsPathSafe_ShouldRejectNullAndWhitespacePaths()
    {
        var baseDir = this.sharedBaseDir;
        Assert.Null(ReceiveQuietly(() => PathValidator.ResolveSecurePath(null!, baseDir)));
        Assert.Null(ReceiveQuietly(() => PathValidator.ResolveSecurePath(string.Empty, baseDir)));
        Assert.Null(ReceiveQuietly(() => PathValidator.ResolveSecurePath("   ", baseDir)));
        Assert.False(PathValidator.IsPathSafe(string.Empty, baseDir));
        Assert.False(PathValidator.IsPathSafe("   ", baseDir));
    }

    [Property]
    public void PathValidator_ResolveSecurePath_ShouldHandleExistingFilesAndSymlinksConsistentlyWithIsPathSafe(
        PathSegmentPropertyInput input,
        bool linkPointsInsideBase)
    {
        int iter = Interlocked.Increment(ref this.iterationCounter);
        var iterRoot = Path.Combine(this.tempRoot, string.Concat("sym_iter_", iter.ToString(CultureInfo.InvariantCulture)));
        var baseDir = Path.Combine(iterRoot, "base");
        var outsideDir = Path.Combine(iterRoot, "out");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outsideDir);

        var safeName = BuildSafeSegment(input.RawText, input.ReplacementChar, 7);
        var targetRoot = linkPointsInsideBase
            ? Path.Combine(baseDir, "real_inside")
            : outsideDir;
        Directory.CreateDirectory(targetRoot);

        // Create an existing file on disk so PathValidator.ResolveLinkTargetWithSuffix exercises
        // the File.Exists(fullPath) ? new FileInfo(fullPath) and FileInfo.Directory branches.
        var existingFilePath = Path.Combine(targetRoot, string.Concat(safeName, ".txt"));
        File.WriteAllText(existingFilePath, "content", Encoding.UTF8);

        var directResolved = ReceiveQuietly(() => PathValidator.ResolveSecurePath(existingFilePath, baseDir));
        var directSafe = PathValidator.IsPathSafe(existingFilePath, baseDir);
        Assert.Equal(linkPointsInsideBase, directSafe);
        Assert.Equal(linkPointsInsideBase, directResolved is not null);

        var symlinkDir = Path.Combine(baseDir, string.Concat("sym_", safeName));
        if (TryCreateDirectorySymlink(symlinkDir, targetRoot))
        {
            var fileViaSymlink = Path.Combine(symlinkDir, string.Concat(safeName, ".txt"));
            var symResolved = ReceiveQuietly(() => PathValidator.ResolveSecurePath(fileViaSymlink, baseDir));
            var symSafe = PathValidator.IsPathSafe(fileViaSymlink, baseDir);

            Assert.Equal(linkPointsInsideBase, symSafe);
            Assert.Equal(linkPointsInsideBase, symResolved is not null);
        }
    }

    [Property]
    public void SourcePathSanitizer_TryNormalize_ShouldNormalizeSeparatorsAndRejectTraversal(
        PathSegmentPropertyInput input,
        byte depthSeed,
        bool useBackslashes)
    {
        var segmentCount = (depthSeed % 4) + 1;
        var segments = new string[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            var seg = BuildSafeSegment(input.RawText, input.ReplacementChar, (byte)(depthSeed + i));
            segments[i] = i == segmentCount - 1 ? string.Concat(seg, ".pdf") : seg;
        }

        var separator = useBackslashes ? "\\" : "//";
        var rawRelativePath = string.Concat("  ", string.Join(separator, segments), "  ");

        Assert.True(SourcePathSanitizer.TryNormalize(rawRelativePath, out var normalized, out var error));
        Assert.Null(error);
        Assert.Equal(string.Join('/', segments), normalized);
        Assert.False(normalized.StartsWith('/'));
        Assert.False(normalized.EndsWith('/'));
        Assert.False(normalized.Contains('\\', StringComparison.Ordinal));
        Assert.False(normalized.Contains("//", StringComparison.Ordinal));

        foreach (var part in normalized.Split('/'))
        {
            Assert.NotEqual(".", part);
            Assert.NotEqual("..", part);
            Assert.False(part.EndsWith('.'));
            Assert.False(part.EndsWith(' '));
        }

        // Rooted, UNC, drive-qualified, leading-invalid-char, and parent-traversal variants must be rejected.
        Assert.False(SourcePathSanitizer.TryNormalize(string.Concat(":", normalized), out _, out var colonErr));
        Assert.Contains("':'", colonErr, StringComparison.Ordinal);
        Assert.False(SourcePathSanitizer.TryNormalize(string.Concat("/", normalized), out _, out _));
        Assert.False(SourcePathSanitizer.TryNormalize(string.Concat("\\\\server\\share\\", normalized), out _, out _));
        Assert.False(SourcePathSanitizer.TryNormalize(string.Concat("C:\\", normalized), out _, out _));
        Assert.False(SourcePathSanitizer.TryNormalize(string.Concat(segments[0], "/../", segments[^1]), out _, out _));
        Assert.False(SourcePathSanitizer.TryNormalize(string.Concat(segments[0], "/./", segments[^1]), out _, out _));
    }

    [Property]
    public void SourcePathSanitizer_TryNormalize_ShouldBeIdempotentAndMatchOracle(PathSegmentPropertyInput input)
    {
        var raw = input.RawText;
        var expectedValid = IsExpectedValidSourceRelativePath(raw);
        var actualValid = SourcePathSanitizer.TryNormalize(raw, out var normalized, out var error);

        Assert.Equal(expectedValid, actualValid);

        if (actualValid)
        {
            Assert.Null(error);
            Assert.NotEmpty(normalized);

            // Idempotence: normalizing an already-normalized path yields the exact same path.
            Assert.True(SourcePathSanitizer.TryNormalize(normalized, out var reNormalized, out var secondError));
            Assert.Null(secondError);
            Assert.Equal(normalized, reNormalized);
        }
        else
        {
            Assert.Equal(string.Empty, normalized);
            Assert.False(string.IsNullOrEmpty(error));
        }
    }

    [Property]
    public void SourceFileTypeMap_TryFromExtension_ShouldMapSupportedExtensionsAndRejectInvalidInputs(
        byte mappingIndex,
        byte caseMask,
        byte unsupportedIndex)
    {
        var (ext, expectedType) = SupportedExtensionMappings[mappingIndex % SupportedExtensionMappings.Length];
        var mixedCaseExt = ApplyCaseMask(ext, caseMask);

        // SourceFileTypeMap.TryFromExtension expects lowercased extension with leading dot.
        Assert.True(SourceFileTypeMap.TryFromExtension(mixedCaseExt.ToLowerInvariant(), out var resolvedType));
        Assert.Equal(expectedType, resolvedType);
        Assert.True(FileGeneratorFactory.IsKnownType(resolvedType));

        // Canonical type with leading dot round-trips to itself.
        Assert.True(SourceFileTypeMap.TryFromExtension(string.Concat(".", resolvedType), out var roundTripType));
        Assert.Equal(resolvedType, roundTripType);

        // Without leading dot, TryFromExtension rejects the input.
        var withoutDot = ext.TrimStart('.');
        Assert.False(SourceFileTypeMap.TryFromExtension(withoutDot, out var noDotType));
        Assert.Equal(string.Empty, noDotType);

        // Unsupported extensions are rejected.
        var unsupported = UnsupportedExtensions[unsupportedIndex % UnsupportedExtensions.Length];
        Assert.False(SourceFileTypeMap.TryFromExtension(unsupported, out var unsupportedType));
        Assert.Equal(string.Empty, unsupportedType);
    }

    [Property]
    public void SourceCsvReader_Read_ShouldRoundTripValidSourceRecordsAndMetadata(
        PathSegmentPropertyInput input,
        byte rowCountSeed,
        byte typeSeed)
    {
        int iter = Interlocked.Increment(ref this.iterationCounter);
        var rowCount = (rowCountSeed % 6) + 1;
        var expectedRows = new List<CsvRowSeed>(rowCount);
        var sb = new StringBuilder();
        sb.AppendLine("FilePath,FileType,ControlNumber,BatesNumber,Custodian,Department");

        for (int i = 0; i < rowCount; i++)
        {
            var (ext, canonicalType) = SupportedExtensionMappings[(typeSeed + i) % SupportedExtensionMappings.Length];
            var safeStem = BuildSafeSegment(input.RawText, input.ReplacementChar, (byte)(i + 1));
            var indexText = (i + 1).ToString(CultureInfo.InvariantCulture);
            var relativePath = string.Concat("folder_", indexText, "/", safeStem, "_", indexText, ext);
            var declaredType = (i % 2 == 0)
                ? ApplyCaseMask(canonicalType, (byte)(typeSeed + i))
                : string.Concat(".", ApplyCaseMask(canonicalType, (byte)(typeSeed + i)));
            var controlNumber = (i % 3 == 0) ? null : string.Concat("DOC_", safeStem, "_", indexText);
            var batesNumber = (i % 2 == 1) ? null : string.Concat("BATES", (1000 + i).ToString(CultureInfo.InvariantCulture));
            var custodian = string.Concat("Custodian_", safeStem, "_", indexText);
            var department = (i % 2 == 0) ? string.Concat("Legal_", indexText) : string.Empty;

            expectedRows.Add(new CsvRowSeed(
                relativePath,
                declaredType,
                canonicalType,
                controlNumber,
                batesNumber,
                custodian,
                department));

            var rawCsvPath = (i % 2 == 0) ? relativePath : relativePath.Replace('/', '\\');
            sb.Append(EscapeCsvField(rawCsvPath)).Append(',')
              .Append(EscapeCsvField(declaredType)).Append(',')
              .Append(EscapeCsvField(controlNumber ?? string.Empty)).Append(',')
              .Append(EscapeCsvField(batesNumber ?? string.Empty)).Append(',')
              .Append(EscapeCsvField(custodian)).Append(',')
              .Append(EscapeCsvField(department)).AppendLine();
        }

        var csvPath = Path.Combine(this.tempRoot, string.Concat("source_", iter.ToString(CultureInfo.InvariantCulture), ".csv"));
        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);

        Assert.True(SourceCsvReader.TryRead(csvPath, out var records, out var error));
        Assert.Null(error);
        Assert.Equal(expectedRows.Count, records.Count);

        for (int i = 0; i < expectedRows.Count; i++)
        {
            var expected = expectedRows[i];
            var actual = records[i];

            Assert.Equal(expected.RelativePath, actual.RelativePath);
            Assert.Equal(expected.CanonicalFileType, actual.FileType);
            Assert.Equal(expected.ControlNumber, actual.ControlNumber);
            Assert.Equal(expected.BatesNumber, actual.BatesNumber);
            Assert.NotNull(actual.Metadata);
            Assert.Equal(expected.Custodian, actual.Metadata["Custodian"]);

            if (string.IsNullOrEmpty(expected.Department))
            {
                Assert.False(actual.Metadata.ContainsKey("Department"));
            }
            else
            {
                Assert.Equal(expected.Department, actual.Metadata["Department"]);
            }
        }
    }

    [Property]
    public void SourceCsvReader_TryRead_ShouldPreserveQuotedCommasEscapedQuotesAndMultilineMetadata(
        PathSegmentPropertyInput input,
        bool useCrLf,
        bool emitBom)
    {
        int iter = Interlocked.Increment(ref this.iterationCounter);
        var newline = useCrLf ? "\r\n" : "\n";
        var safeStem = BuildSafeSegment(input.RawText, input.ReplacementChar, 11);
        var sanitizedRaw = (input.RawText ?? string.Empty)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);
        var complexNotes = string.Concat("Value, with \"quotes\", ", sanitizedRaw, "\nSecond line ", safeStem);

        var csvContent = string.Concat(
            "FilePath,FileType,Notes,EmptyMetadata", newline,
            newline,
            EscapeCsvField(string.Concat("docs/", safeStem, ".pdf")), ",pdf,", EscapeCsvField(complexNotes), ",", newline);

        var csvPath = Path.Combine(this.tempRoot, string.Concat("quoted_", iter.ToString(CultureInfo.InvariantCulture), ".csv"));
        File.WriteAllText(csvPath, csvContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: emitBom));

        Assert.True(SourceCsvReader.TryRead(csvPath, out var records, out _, out var sourceMetadataKeys, out var error));
        Assert.Null(error);
        var record = Assert.Single(records);
        Assert.Equal(string.Concat("docs/", safeStem, ".pdf"), record.RelativePath);
        Assert.Equal("pdf", record.FileType);
        Assert.NotNull(record.Metadata);
        Assert.Equal(complexNotes, record.Metadata["Notes"]);
        Assert.False(record.Metadata.ContainsKey("EmptyMetadata"));
        Assert.Equal(2, sourceMetadataKeys!.Count);
        Assert.Contains("Notes", sourceMetadataKeys);
        Assert.Contains("EmptyMetadata", sourceMetadataKeys);
    }

    [Property]
    public void SourceRecordIntake_TryAdd_ShouldEnforceUniqueIdentitiesAndConvertWorkItems(
        PathSegmentPropertyInput input,
        byte extIndex)
    {
        var (ext, canonicalType) = SupportedExtensionMappings[extIndex % SupportedExtensionMappings.Length];
        var stem = BuildSafeSegment(input.RawText, input.ReplacementChar, 21);
        var relPath1 = string.Concat("vol1/", stem, "_1", ext);
        var relPath2 = string.Concat("vol1/", stem, "_2", ext);
        var control1 = string.Concat("CTRL-", stem, "-1");
        var bates1 = string.Concat("BATES-", stem, "-1");

        var intake = new SourceRecordIntake("PropertyTestSource", maxRecords: 10);

        // 1. Valid record with inferred File Type (fileTypeText: null) succeeds.
        Assert.True(intake.TryAdd(relPath1, null, control1, bates1, null, "Row 1", out var err1));
        Assert.Null(err1);

        // 2. Case-insensitive duplicate path is rejected.
        Assert.False(intake.TryAdd(relPath1.ToUpperInvariant(), canonicalType, "CTRL-OTHER", "BATES-OTHER", null, "Row 2", out var dupPathErr));
        Assert.NotNull(dupPathErr);

        // 3. Case-insensitive duplicate ControlNumber is rejected.
        Assert.False(intake.TryAdd(relPath2, canonicalType, control1.ToLowerInvariant(), "BATES-OTHER", null, "Row 3", out var dupCtrlErr));
        Assert.NotNull(dupCtrlErr);

        // 4. Case-insensitive duplicate BatesNumber is rejected.
        Assert.False(intake.TryAdd(relPath2, canonicalType, "CTRL-OTHER", bates1.ToLowerInvariant(), null, "Row 4", out var dupBatesErr));
        Assert.NotNull(dupBatesErr);

        // 5. ControlNumber or BatesNumber with path separators or control chars is rejected.
        Assert.False(intake.TryAdd(relPath2, canonicalType, string.Concat(control1, "/bad"), null, null, "Row 5", out var badCtrlErr));
        Assert.NotNull(badCtrlErr);
        Assert.False(intake.TryAdd(relPath2, canonicalType, null, string.Concat(bates1, "\tbad"), null, "Row 6", out var badBatesErr));
        Assert.NotNull(badBatesErr);

        // 6. Mismatched extension vs declared FileType is rejected.
        var mismatchedType = string.Equals(canonicalType, "pdf", StringComparison.Ordinal) ? "eml" : "pdf";
        Assert.False(intake.TryAdd(relPath2, mismatchedType, null, null, null, "Row 7", out var mismatchErr));
        Assert.NotNull(mismatchErr);

        // 7. Build succeeds and ToWorkItem splits FolderName and FileName accurately.
        Assert.True(intake.TryBuild(out var records, out var buildErr));
        Assert.Null(buildErr);
        var single = Assert.Single(records);
        var workItem = single.ToWorkItem(42);
        Assert.Equal(42L, workItem.Index);
        Assert.Equal("vol1", workItem.FolderName);
        Assert.Equal(string.Concat(stem, "_1", ext), workItem.FileName);
        Assert.Equal(relPath1, workItem.FilePathInZip);
        Assert.Equal(canonicalType, workItem.FileType);
        Assert.Equal(control1, workItem.ControlNumberOverride);
        Assert.Equal(bates1, workItem.BatesNumberOverride);
    }

    [Property]
    public void SourceRecordIntake_TryMapCsvColumns_ShouldNormalizeHeaderAliasesAndRejectAmbiguity(
        byte controlAliasSeed,
        byte batesAliasSeed,
        byte caseMask)
    {
        var filePathHeader = ApplyCaseMask((controlAliasSeed % 2 == 0) ? "File_Path" : "File - Path", caseMask);
        var fileTypeHeader = ApplyCaseMask((batesAliasSeed % 2 == 0) ? "File-Type" : "File Type", caseMask);
        var controlHeader = ApplyCaseMask((controlAliasSeed % 2 == 0) ? "Control_Number" : "Doc Id", caseMask);
        var batesHeader = (batesAliasSeed % 3) switch
        {
            0 => ApplyCaseMask("Bates_Number", caseMask),
            1 => ApplyCaseMask("Beg-Bates", caseMask),
            _ => ApplyCaseMask("Bates", caseMask)
        };

        var headers = new[] { filePathHeader, fileTypeHeader, controlHeader, batesHeader, "Custodian Name" };
        Assert.True(SourceRecordIntake.TryMapCsvColumns(headers, out var layout, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, layout.PathIndex);
        Assert.Equal(1, layout.TypeIndex);
        Assert.Equal(2, layout.ControlIndex);
        Assert.Equal(3, layout.BatesIndex);
        var metaCol = Assert.Single(layout.MetadataColumns);
        Assert.Equal(4, metaCol.Index);
        Assert.Equal("Custodian Name", metaCol.Name);

        // Conflicting aliases (both ControlNumber and DocId) must be rejected.
        var ambiguousHeaders = new[]
        {
            filePathHeader,
            fileTypeHeader,
            ApplyCaseMask("Control_Number", caseMask),
            ApplyCaseMask("Doc-Id", caseMask),
        };
        Assert.False(SourceRecordIntake.TryMapCsvColumns(ambiguousHeaders, out _, out var ambiguousErr));
        Assert.False(string.IsNullOrEmpty(ambiguousErr));

        // Duplicate normalized metadata headers must be rejected.
        var duplicateMetadataHeaders = new[]
        {
            filePathHeader,
            fileTypeHeader,
            ApplyCaseMask("Custodian_Name", caseMask),
            ApplyCaseMask("custodian-name", (byte)~caseMask),
        };
        Assert.False(SourceRecordIntake.TryMapCsvColumns(duplicateMetadataHeaders, out _, out var dupMetaErr));
        Assert.False(string.IsNullOrEmpty(dupMetaErr));
    }

    [Property]
    public void NamingConventionHelper_Convert_ShouldBeIdempotentAcrossAllConventions(
        PathSegmentPropertyInput input,
        byte conventionSeed,
        byte tokenSeed)
    {
        var convention = NamingConventions[conventionSeed % NamingConventions.Length];

        // 1. Multi-token realistic field names are strictly idempotent in one pass across all conventions.
        var token1 = SampleFieldTokens[tokenSeed % SampleFieldTokens.Length];
        var token2 = SampleFieldTokens[(tokenSeed + 3) % SampleFieldTokens.Length];
        var token3 = SampleFieldTokens[(tokenSeed + 7) % SampleFieldTokens.Length];
        var compositeFieldName = string.Concat(token1, "_", token2, " - ", token3);

        var firstPass = NamingConventionHelper.ApplyConvention(compositeFieldName, convention);
        var secondPass = NamingConventionHelper.ApplyConvention(firstPass, convention);
        Assert.Equal(firstPass, secondPass);

        // Padded/mixed-case convention specification produces the exact same output.
        var noisyConvention = string.Concat("  ", ApplyCaseMask(convention, conventionSeed), "  ");
        Assert.Equal(firstPass, NamingConventionHelper.ApplyConvention(compositeFieldName, noisyConvention));

        // 2. Arbitrary raw strings: UPPERCASE, lowercase, and snake_case stabilize after 1 pass,
        // and PascalCase stabilizes after at most 2 passes (when 1-char words coalesce into an acronym).
        var raw = input.RawText ?? string.Empty;
        var rawPass1 = NamingConventionHelper.ApplyConvention(raw, convention);
        var rawPass2 = NamingConventionHelper.ApplyConvention(rawPass1, convention);
        var rawPass3 = NamingConventionHelper.ApplyConvention(rawPass2, convention);
        Assert.Equal(rawPass2, rawPass3);
    }

    [Property]
    public void NamingConventionHelper_ApplyConvention_ShouldSatisfyCasingAndBufferThresholdInvariants(
        PathSegmentPropertyInput input,
        byte tokenSeed,
        bool exceedStackallocThreshold)
    {
        var baseToken = SampleFieldTokens[tokenSeed % SampleFieldTokens.Length];
        var secondToken = SampleFieldTokens[(tokenSeed + 5) % SampleFieldTokens.Length];
        var dynamicToken = BuildSafeSegment(input.RawText, input.ReplacementChar, tokenSeed);

        // When exceedStackallocThreshold is true, build a word > 256 chars to exercise the heap fallback in ToPascalCase.
        var longTail = exceedStackallocThreshold ? new string('x', 280) : dynamicToken;
        var fieldName = string.Concat(baseToken, longTail, "_DOCID_", secondToken);

        var upper = NamingConventionHelper.ApplyConvention(fieldName, "UPPERCASE");
        Assert.Equal(fieldName.ToUpperInvariant(), upper);

        var lower = NamingConventionHelper.ApplyConvention(fieldName, "lowercase");
        Assert.Equal(fieldName.ToLowerInvariant(), lower);

        var snake = NamingConventionHelper.ApplyConvention(fieldName, "snake_case");
        Assert.Equal(snake.ToLowerInvariant(), snake);
        Assert.False(snake.Contains(' ', StringComparison.Ordinal));
        Assert.False(snake.Contains('-', StringComparison.Ordinal));
        Assert.False(snake.Contains("__", StringComparison.Ordinal));

        var pascal = NamingConventionHelper.ApplyConvention(fieldName, "PascalCase");
        Assert.False(pascal.Contains('_', StringComparison.Ordinal));
        Assert.False(pascal.Contains('-', StringComparison.Ordinal));
        Assert.False(pascal.Contains(' ', StringComparison.Ordinal));
        Assert.True(char.IsUpper(pascal[0]));
        Assert.Equal(pascal, NamingConventionHelper.ToPascalCase(fieldName));

        // Acronym-prefixed field names split camel/acronym boundaries accurately in PascalCase and snake_case.
        var acronymField = string.Concat("DocID_", secondToken);
        Assert.Equal(string.Concat("DocId", NamingConventionHelper.ToPascalCase(secondToken)), NamingConventionHelper.ApplyConvention(acronymField, "PascalCase"));
        Assert.Equal(string.Concat("doc_id_", secondToken.ToLowerInvariant()), NamingConventionHelper.ApplyConvention(acronymField, "snake_case"));

        // Unknown or null convention preserves original field name.
        Assert.Equal(fieldName, NamingConventionHelper.ApplyConvention(fieldName, null));
        Assert.Equal(fieldName, NamingConventionHelper.ApplyConvention(fieldName, " unrecognized "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void NamingConventionHelper_ApplyConvention_ShouldPreserveEmptyAndWhitespaceInput(string whitespaceOrEmpty)
    {
        Assert.Equal(whitespaceOrEmpty, NamingConventionHelper.ApplyConvention(whitespaceOrEmpty, "PascalCase"));
        Assert.Equal(whitespaceOrEmpty, NamingConventionHelper.ApplyConvention(whitespaceOrEmpty, "snake_case"));
        Assert.Equal(whitespaceOrEmpty, NamingConventionHelper.ApplyConvention(whitespaceOrEmpty, "UPPERCASE"));
        Assert.Equal(whitespaceOrEmpty, NamingConventionHelper.ApplyConvention(whitespaceOrEmpty, "lowercase"));
    }

    private static bool TryValidateProductionIdSegment(string segment)
    {
        return ReceiveQuietly(() =>
        {
            var module = new ProductionModule();
            if (!module.TryApply("--production-set", null))
            {
                return false;
            }

            if (!module.TryApply("--production-id", segment))
            {
                return false;
            }

            return module.TryBuild(out _);
        });
    }

    private static T ReceiveQuietly<T>(Func<T> action)
    {
        return ConsoleOutputCapture.RunWithCapturedOutput(action).result;
    }

    private static bool IsExpectedSafeProductionIdSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value[^1] is ' ' or '.')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            {
                return false;
            }
        }

        var trimmed = value.TrimEnd(' ', '.');
        if (trimmed.Length == 0 ||
            string.Equals(trimmed, ".", StringComparison.Ordinal) ||
            string.Equals(trimmed, "..", StringComparison.Ordinal))
        {
            return false;
        }

        var dotIndex = trimmed.IndexOf('.', StringComparison.Ordinal);
        var stem = dotIndex >= 0 ? trimmed[..dotIndex] : trimmed;

        foreach (var reserved in ProductionIdReservedDeviceNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExpectedValidSourceRelativePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        var trimmed = rawPath.Trim();
        if (trimmed.AsSpan().IndexOfAny(InvalidSourcePathChars) >= 0)
        {
            return false;
        }

        foreach (var c in trimmed)
        {
            if (c < 32)
            {
                return false;
            }
        }

        var unified = trimmed.Replace('\\', '/');
        if (unified.StartsWith('/'))
        {
            return false;
        }

        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal) ||
                string.Equals(segment, ".", StringComparison.Ordinal) ||
                segment.EndsWith('.') ||
                segment.EndsWith(' '))
            {
                return false;
            }

            var dotIndex = segment.IndexOf('.', StringComparison.Ordinal);
            var stem = (dotIndex >= 0 ? segment[..dotIndex] : segment).TrimEnd(' ', '.');
            foreach (var reserved in AsciiReservedDeviceNames)
            {
                if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string BuildSafeSegment(string? rawText, char replacementChar, byte suffix)
    {
        var replacement = replacementChar is '_' or '-' ? replacementChar : '_';
        var sb = new StringBuilder("SEG_");

        if (!string.IsNullOrEmpty(rawText))
        {
            foreach (var c in rawText)
            {
                if ((c is >= 'a' and <= 'z') ||
                    (c is >= 'A' and <= 'Z') ||
                    (c is >= '0' and <= '9') ||
                    c is '_' or '-')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(replacement);
                }

                if (sb.Length >= 24)
                {
                    break;
                }
            }
        }

        sb.Append('_').Append(suffix.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string ApplyCaseMask(string input, byte mask)
    {
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var bit = (mask >> (i % 8)) & 1;
            chars[i] = bit == 1
                ? char.ToUpperInvariant(chars[i])
                : char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    private static string EscapeCsvField(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return string.Concat("\"", escaped, "\"");
    }

    private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() &&
            ex is UnauthorizedAccessException or PlatformNotSupportedException or System.ComponentModel.Win32Exception or IOException)
        {
            // Unprivileged Windows processes without Developer Mode may lack SeCreateSymbolicLinkPrivilege.
            return false;
        }
    }
}
