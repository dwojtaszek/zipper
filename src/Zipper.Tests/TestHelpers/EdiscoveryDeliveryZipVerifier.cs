using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Zipper.Validation;

namespace Zipper.Tests.TestHelpers;

/// <summary>
/// Structured metrics from an e-discovery delivery verification,
/// reporting documents, Native Files, and ZIP members separately.
/// </summary>
public sealed record EdiscoveryDeliveryReport(
    long DocumentCount,
    long NativeFileCount,
    long ZipMemberCount,
    int VolumeCount)
{
    public override string ToString() =>
        $"Documents: {DocumentCount}, Native Files: {NativeFileCount}, ZIP members: {ZipMemberCount}, Volumes: {VolumeCount}";
}

/// <summary>
/// Independent reader and verification harness for e-discovery ZIP deliveries
/// (both Production Set mode with --production-zip and Standard mode with --include-load-file).
/// </summary>
internal static class EdiscoveryDeliveryZipVerifier
{
    /// <summary>
    /// Verifies a Production Set ZIP delivery against its on-disk artifacts and e-discovery contract.
    /// </summary>
    public static async Task<EdiscoveryDeliveryReport> VerifyProductionSetZipDeliveryAsync(
        string zipFilePath,
        string productionDirectory,
        FileGenerationRequest request,
        ITestOutputHelper? output = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(zipFilePath);
        ArgumentException.ThrowIfNullOrEmpty(productionDirectory);
        ArgumentNullException.ThrowIfNull(request);

        Assert.True(File.Exists(zipFilePath), $"Production ZIP file not found at '{zipFilePath}'.");
        Assert.True(Directory.Exists(productionDirectory), $"Production directory not found at '{productionDirectory}'.");

        var prodName = Path.GetFileName(Path.TrimEndingDirectorySeparator(productionDirectory));
        var rootPrefix = prodName + "/";
        var parentDir = Path.GetDirectoryName(Path.GetFullPath(productionDirectory))!;

        // 1. Independent reader: inventory, compression method, decompression/CRC, and bytes vs disk.
        ZipAssert.AssertProductionZipMatchesDisk(zipFilePath, productionDirectory, request.Output.CompressionMethod);

        using var archive = ZipFile.OpenRead(zipFilePath);
        var archiveEntryNames = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToHashSet(StringComparer.Ordinal);

        var diskFiles = Directory.GetFiles(productionDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(parentDir, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        AssertDeliveryTypeCoverage(archive.Entries);

        // 2. Production Set validation pipeline: run PostGenerationValidator.
        var validationReport = ProductionSetPostValidator.Validate(productionDirectory, request);
        Assert.Equal("passed", validationReport.Status);
        Assert.Equal(0, validationReport.ErrorCount);
        Assert.Equal(0, validationReport.WarningCount);
        Assert.Empty(validationReport.Findings);

        // 3. Golden hash-manifest tooling: disk and archive manifests must describe the same bytes.
        var diskManifest = ParseManifest(GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(productionDirectory));
        var archiveManifest = ParseManifest(GoldenBaselineHarness.GenerateTimestampNormalizedSha256ManifestForArchive(zipFilePath));
        Assert.NotEmpty(diskManifest);
        var archiveManifestPrefix = $"{Path.GetFileName(zipFilePath)}::{prodName}/";
        foreach (var (relativePath, hash) in diskManifest.Where(entry => !entry.Key.Contains("::", StringComparison.Ordinal)))
        {
            Assert.True(
                archiveManifest.TryGetValue($"{archiveManifestPrefix}{relativePath}", out var archiveHash),
                $"Archive manifest is missing '{relativePath}'.");
            Assert.Equal(hash, archiveHash);
        }

        // 4. Assert DAT/OPT references resolve inside the delivery (both disk and ZIP).
        char colDelim = request.Delimiters?.GetColumnChar() ?? '\u0014';
        char quoteDelim = request.Delimiters?.GetQuoteChar() ?? '\u00fe';

        var datPath = Path.Combine(productionDirectory, "DATA", "loadfile.dat");
        Assert.True(File.Exists(datPath), "loadfile.dat must exist on disk.");
        var datLines = (await File.ReadAllLinesAsync(datPath, Encoding.UTF8))
            .Where(l => l.Length > 0)
            .ToList();
        Assert.True(datLines.Count > 1, "loadfile.dat must contain header and at least one record.");

        var header = DelimitedTestParser.ParseLine(datLines[0], colDelim, quoteDelim).ToArray();
        int docIdIdx = Array.FindIndex(header, h => string.Equals(h, "DOCID", StringComparison.OrdinalIgnoreCase));
        int nativeIdx = Array.FindIndex(header, h => string.Equals(h, "NATIVE_PATH", StringComparison.OrdinalIgnoreCase));
        int imageIdx = Array.FindIndex(header, h => string.Equals(h, "IMAGE_PATH", StringComparison.OrdinalIgnoreCase));
        int textIdx = Array.FindIndex(header, h => string.Equals(h, "TEXT_PATH", StringComparison.OrdinalIgnoreCase));
        int begAttachIdx = Array.FindIndex(header, h => string.Equals(h, "BEGATTACH", StringComparison.OrdinalIgnoreCase));
        int endAttachIdx = Array.FindIndex(header, h => string.Equals(h, "ENDATTACH", StringComparison.OrdinalIgnoreCase));
        int parentDocIdIdx = Array.FindIndex(header, h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase));

        Assert.True(docIdIdx >= 0, "DOCID column missing from DAT header.");
        Assert.True(nativeIdx >= 0, "NATIVE_PATH column missing from DAT header.");
        Assert.True(imageIdx >= 0, "IMAGE_PATH column missing from DAT header.");
        Assert.True(textIdx >= 0, "TEXT_PATH column missing from DAT header.");
        Assert.True(begAttachIdx >= 0, "BEGATTACH column missing from DAT header.");
        Assert.True(endAttachIdx >= 0, "ENDATTACH column missing from DAT header.");
        Assert.True(parentDocIdIdx >= 0, "PARENTDOCID column missing from DAT header.");

        var familyRecords = new List<FamilyRecord>();
        for (int i = 1; i < datLines.Count; i++)
        {
            var cols = DelimitedTestParser.ParseLine(datLines[i], colDelim, quoteDelim).ToArray();
            Assert.Equal(header.Length, cols.Length);

            AssertResolves(cols[nativeIdx], "native", rootPrefix, diskFiles, archiveEntryNames);
            AssertResolves(cols[imageIdx], "image", rootPrefix, diskFiles, archiveEntryNames);
            Assert.False(string.IsNullOrEmpty(cols[textIdx]), $"DAT row {i} has no Extracted Text reference.");
            AssertResolves(cols[textIdx], "text", rootPrefix, diskFiles, archiveEntryNames);

            familyRecords.Add(new FamilyRecord(
                cols[docIdIdx],
                cols[begAttachIdx],
                cols[endAttachIdx],
                cols[parentDocIdIdx]));
        }

        // Family membership survives: every child resolves to a parent row, regardless of row order.
        var familyParentIds = familyRecords
            .Where(r => string.IsNullOrEmpty(r.ParentDocId) && !string.Equals(r.BegAttach, r.EndAttach, StringComparison.Ordinal))
            .Select(r => r.DocId)
            .ToHashSet(StringComparer.Ordinal);
        var familyChildren = familyRecords.Where(r => !string.IsNullOrEmpty(r.ParentDocId)).ToList();
        foreach (var child in familyChildren)
        {
            Assert.Equal(child.BegAttach, child.ParentDocId);
            Assert.Contains(child.ParentDocId, familyParentIds);
        }

        AssertFamilyRanges(familyRecords);

        // OPT resolution.
        var optPath = Path.Combine(productionDirectory, "DATA", "loadfile.opt");
        Assert.True(File.Exists(optPath), "loadfile.opt must exist on disk.");
        var optLines = (await File.ReadAllLinesAsync(optPath, Encoding.UTF8))
            .Where(l => l.Length > 0)
            .ToList();
        Assert.NotEmpty(optLines);

        foreach (var optLine in optLines)
        {
            var parts = optLine.Split(',');
            Assert.True(parts.Length >= 7, $"OPT line had {parts.Length} fields, expected >= 7: {optLine}");
            AssertResolves(parts[2].Trim(), "OPT image", rootPrefix, diskFiles, archiveEntryNames);
        }

        // 5. Family membership survival check.
        if (request.Metadata.WithFamilies && request.Output.HasFileType("eml") && request.LoadFile.AttachmentRate > 0)
        {
            Assert.NotEmpty(familyParentIds);
            Assert.NotEmpty(familyChildren);
        }

        // 6. Manifest and validation counts agree.
        var manifestPath = Path.Combine(productionDirectory, "_manifest.json");
        Assert.True(File.Exists(manifestPath), "_manifest.json must exist.");
        using var manifestDoc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var manifestRoot = manifestDoc.RootElement;

        int volumeCount = manifestRoot.GetProperty("volumeCount").GetInt32();
        long nativeFileCount = manifestRoot.GetProperty("nativeFileCount").GetInt64();
        long parentNativeFileCount = manifestRoot.GetProperty("parentNativeFileCount").GetInt64();
        long attachmentNativeFileCount = manifestRoot.GetProperty("attachmentNativeFileCount").GetInt64();

        Assert.Equal(nativeFileCount, parentNativeFileCount + attachmentNativeFileCount);
        Assert.Equal((int)nativeFileCount, validationReport.CheckedFileCounts["native"]);

        // Actual Native Files on disk under NATIVES/.
        var actualNativeFiles = Directory.GetFiles(Path.Combine(productionDirectory, "NATIVES"), "*", SearchOption.AllDirectories);
        Assert.Equal(nativeFileCount, (long)actualNativeFiles.Length);

        // Actual volume subdirectories.
        var actualVolumeDirs = Directory.GetDirectories(Path.Combine(productionDirectory, "NATIVES"), "VOL*");
        Assert.Equal(volumeCount, actualVolumeDirs.Length);

        long totalDocuments = datLines.Count - 1;
        // CheckedLoadFileRowCounts["dat"] includes the header row.
        Assert.Equal((int)totalDocuments + 1, validationReport.CheckedLoadFileRowCounts["dat"]);
        Assert.Equal(optLines.Count, validationReport.CheckedLoadFileRowCounts["opt"]);

        // 7. Report documents, Native Files, and ZIP members separately.
        var report = new EdiscoveryDeliveryReport(
            DocumentCount: totalDocuments,
            NativeFileCount: nativeFileCount,
            ZipMemberCount: archive.Entries.Count,
            VolumeCount: volumeCount);

        output?.WriteLine($"[E-Discovery Delivery Report] {report}");
        return report;
    }

    /// <summary>
    /// Verifies a Standard mode ZIP delivery with included load files against its e-discovery contract.
    /// </summary>
    public static async Task<EdiscoveryDeliveryReport> VerifyStandardModeZipDeliveryAsync(
        string zipFilePath,
        FileGenerationRequest request,
        ITestOutputHelper? output = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(zipFilePath);
        ArgumentNullException.ThrowIfNull(request);

        Assert.True(File.Exists(zipFilePath), $"Standard ZIP archive not found at '{zipFilePath}'.");

        // Independent reader: duplicate names, decompression, and CRC integrity.
        ZipAssert.AssertValidZip(zipFilePath);

        using var archive = ZipFile.OpenRead(zipFilePath);
        var archiveEntryNames = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToHashSet(StringComparer.Ordinal);

        AssertDeliveryTypeCoverage(archive.Entries);

        // Golden hash-manifest tooling on the archive.
        var archiveManifest = ParseManifest(GoldenBaselineHarness.GenerateTimestampNormalizedSha256ManifestForArchive(zipFilePath));
        Assert.NotEmpty(archiveManifest);

        // Locate included DAT load file in archive root.
        var datEntry = archive.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(datEntry);

        char colDelim = request.Delimiters?.GetColumnChar() ?? '\u0014';
        char quoteDelim = request.Delimiters?.GetQuoteChar() ?? '\u00fe';

        using var datReader = new StreamReader(datEntry!.Open(), Encoding.UTF8);
        var datContent = await datReader.ReadToEndAsync();
        var datLines = datContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(datLines.Length > 1, "Standard DAT must contain header and records.");

        var header = DelimitedTestParser.ParseLine(datLines[0], colDelim, quoteDelim).ToArray();
        int filePathIdx = Array.FindIndex(header, h => string.Equals(h, "File Path", StringComparison.OrdinalIgnoreCase));
        int fileSizeIdx = Array.FindIndex(header, h => string.Equals(h, "File Size", StringComparison.OrdinalIgnoreCase));
        int extractedTextIdx = Array.FindIndex(header, h => string.Equals(h, "Extracted Text", StringComparison.OrdinalIgnoreCase));
        int begAttachIdx = Array.FindIndex(header, h => string.Equals(h, "BEGATTACH", StringComparison.OrdinalIgnoreCase));
        int endAttachIdx = Array.FindIndex(header, h => string.Equals(h, "ENDATTACH", StringComparison.OrdinalIgnoreCase));
        int parentDocIdIdx = Array.FindIndex(header, h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase));
        int controlNumIdx = Array.FindIndex(header, h => string.Equals(h, "Control Number", StringComparison.OrdinalIgnoreCase));

        Assert.True(filePathIdx >= 0, "File Path column missing from Standard DAT header.");
        Assert.True(fileSizeIdx >= 0, "File Size column missing from Standard DAT header.");
        Assert.True(controlNumIdx >= 0, "Control Number column missing from Standard DAT header.");
        Assert.True(extractedTextIdx >= 0, "Extracted Text column missing from Standard DAT header.");
        Assert.True(begAttachIdx >= 0, "BEGATTACH column missing from Standard DAT header.");
        Assert.True(endAttachIdx >= 0, "ENDATTACH column missing from Standard DAT header.");
        Assert.True(parentDocIdIdx >= 0, "PARENTDOCID column missing from Standard DAT header.");

        var familyRecords = new List<FamilyRecord>();
        var nativePaths = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < datLines.Length; i++)
        {
            var cols = DelimitedTestParser.ParseLine(datLines[i], colDelim, quoteDelim).ToArray();
            Assert.Equal(header.Length, cols.Length);

            // Native file path resolves inside ZIP, and its DAT File Size is the pre-packaging size.
            var nativeRel = cols[filePathIdx].Replace('\\', '/');
            Assert.Contains(nativeRel, archiveEntryNames);
            nativePaths.Add(nativeRel);
            Assert.False(string.IsNullOrWhiteSpace(cols[fileSizeIdx]), $"Standard DAT row {i} has no File Size.");
            Assert.True(
                long.TryParse(cols[fileSizeIdx], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expectedFileSize),
                $"Standard DAT row {i} has invalid File Size '{cols[fileSizeIdx]}'.");
            var nativeEntry = archive.GetEntry(nativeRel);
            Assert.NotNull(nativeEntry);
            Assert.Equal(expectedFileSize, nativeEntry!.Length);

            // Extracted Text is enabled for this recipe, so every row must reference a packaged text file.
            var textRel = cols[extractedTextIdx].Replace('\\', '/');
            Assert.False(string.IsNullOrEmpty(textRel), $"Standard DAT row {i} has no Extracted Text reference.");
            Assert.Contains(textRel, archiveEntryNames);

            familyRecords.Add(new FamilyRecord(
                cols[controlNumIdx],
                cols[begAttachIdx],
                cols[endAttachIdx],
                cols[parentDocIdIdx]));
        }

        // Family membership survives: every child resolves to a parent row, regardless of row order.
        var familyParentIds = familyRecords
            .Where(r => string.IsNullOrEmpty(r.ParentDocId) && !string.Equals(r.BegAttach, r.EndAttach, StringComparison.Ordinal))
            .Select(r => r.DocId)
            .ToHashSet(StringComparer.Ordinal);
        var familyChildren = familyRecords.Where(r => !string.IsNullOrEmpty(r.ParentDocId)).ToList();
        foreach (var child in familyChildren)
        {
            Assert.Equal(child.BegAttach, child.ParentDocId);
            Assert.Contains(child.ParentDocId, familyParentIds);
        }

        AssertFamilyRanges(familyRecords);

        if (request.Metadata.WithFamilies && request.Output.HasFileType("eml") && request.LoadFile.AttachmentRate > 0)
        {
            Assert.NotEmpty(familyParentIds);
            Assert.NotEmpty(familyChildren);
        }

        // Count Native Files by their DAT role, so a .txt attachment still counts as a Native.
        long nativeFilesCount = nativePaths.Count;

        long totalDocuments = datLines.Length - 1;

        // 7. Report documents, Native Files, and ZIP members separately.
        var report = new EdiscoveryDeliveryReport(
            DocumentCount: totalDocuments,
            NativeFileCount: nativeFilesCount,
            ZipMemberCount: archive.Entries.Count,
            VolumeCount: 1);

        output?.WriteLine($"[E-Discovery Delivery Report] {report}");
        return report;
    }

    private static void AssertResolves(
        string referencedPath,
        string kind,
        string rootPrefix,
        IReadOnlySet<string> diskFiles,
        IReadOnlySet<string> archiveEntryNames)
    {
        if (string.IsNullOrEmpty(referencedPath))
        {
            return;
        }

        var relativePath = referencedPath.Replace('\\', '/');
        var deliveryPath = $"{rootPrefix}{relativePath}";
        Assert.True(diskFiles.Contains(deliveryPath), $"Load File referenced {kind} file missing on disk: {relativePath}");
        Assert.Contains(deliveryPath, archiveEntryNames);
    }

    private static void AssertDeliveryTypeCoverage(IEnumerable<ZipArchiveEntry> entries)
    {
        var names = entries.Select(e => e.FullName).ToList();
        Assert.Contains(names, n => n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith(".eml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertFamilyRanges(IReadOnlyList<FamilyRecord> familyRecords)
    {
        var parents = familyRecords
            .Where(r => string.IsNullOrEmpty(r.ParentDocId) && !string.Equals(r.BegAttach, r.EndAttach, StringComparison.Ordinal))
            .ToDictionary(r => r.DocId, StringComparer.Ordinal);
        var childrenByParent = familyRecords
            .Where(r => !string.IsNullOrEmpty(r.ParentDocId))
            .GroupBy(r => r.ParentDocId, StringComparer.Ordinal);

        foreach (var group in childrenByParent)
        {
            Assert.True(parents.TryGetValue(group.Key, out var parent), $"Family child parent '{group.Key}' has no parent row.");
            var lastChild = group.OrderBy(r => r.DocId, StringComparer.Ordinal).Last();
            Assert.Equal(lastChild.DocId, parent!.EndAttach);
        }
    }

    private static Dictionary<string, string> ParseManifest(string manifest)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split("  ", 2, StringSplitOptions.None);
            Assert.Equal(2, parts.Length);
            entries.Add(parts[1], parts[0]);
        }

        return entries;
    }

    private sealed record FamilyRecord(
        string DocId,
        string BegAttach,
        string EndAttach,
        string ParentDocId);
}
