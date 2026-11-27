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

/// <summary>Recipe expectation for one Standard DAT row, keyed by Control Number.</summary>
public sealed record StandardDeliveryRow(string NativePath, string TextPath, string ParentDocId);

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

        var normalizedProductionDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(productionDirectory));
        var prodName = Path.GetFileName(normalizedProductionDirectory);
        var rootPrefix = prodName + "/";
        var parentDir = Path.GetDirectoryName(normalizedProductionDirectory)!;

        // 1. Independent reader: inventory, compression method, decompression/CRC, and bytes vs disk.
        ZipAssert.AssertProductionZipMatchesDisk(zipFilePath, normalizedProductionDirectory, request.Output.CompressionMethod);

        using var archive = ZipFile.OpenRead(zipFilePath);
        var archiveEntryNames = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToHashSet(StringComparer.Ordinal);

        var diskFiles = Directory.GetFiles(productionDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(parentDir, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        AssertNativeDeliveryTypeCoverage(
            archive.Entries.Where(e => e.FullName.Contains("/NATIVES/", StringComparison.OrdinalIgnoreCase)).Select(e => e.FullName));
        Assert.Contains(archive.Entries, e => e.FullName.Contains("/TEXT/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Contains("/IMAGES/", StringComparison.OrdinalIgnoreCase) && (e.FullName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)));

        // 2. Production Set validation pipeline: run PostGenerationValidator.
        var validationReport = ProductionSetPostValidator.Validate(productionDirectory, request);
        Assert.Equal("passed", validationReport.Status);
        Assert.Equal(0, validationReport.ErrorCount);
        Assert.Equal(0, validationReport.WarningCount);
        Assert.Empty(validationReport.Findings);

        // The generation run validated these same files before packaging. Matching counts prove
        // the request built from the CLI args is equivalent for every field the validator reads.
        var persistedReportPath = Path.Combine(productionDirectory, "_validation_report.json");
        Assert.True(File.Exists(persistedReportPath), "_validation_report.json must exist.");
        var persistedReport = JsonSerializer.Deserialize<ProductionSetValidationReport>(
            await File.ReadAllTextAsync(persistedReportPath));
        Assert.NotNull(persistedReport);
        Assert.Equal(persistedReport!.Status, validationReport.Status);
        Assert.Equal(persistedReport.ErrorCount, validationReport.ErrorCount);
        Assert.Equal(persistedReport.WarningCount, validationReport.WarningCount);
        Assert.Empty(persistedReport.Findings);
        Assert.Equal(validationReport.Findings.Count, persistedReport.Findings.Count);
        Assert.Equal(persistedReport.CheckedFileCounts.Count, validationReport.CheckedFileCounts.Count);
        foreach (var (kind, count) in validationReport.CheckedFileCounts)
        {
            Assert.Equal(count, persistedReport.CheckedFileCounts[kind]);
        }

        Assert.Equal(persistedReport.CheckedLoadFileRowCounts.Count, validationReport.CheckedLoadFileRowCounts.Count);
        foreach (var (loadFile, count) in validationReport.CheckedLoadFileRowCounts)
        {
            Assert.Equal(count, persistedReport.CheckedLoadFileRowCounts[loadFile]);
        }

        // 3. Golden hash-manifest tooling: disk and archive manifests must describe the same bytes.
        var diskManifest = ParseManifest(GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(productionDirectory));
        var archiveManifest = ParseManifest(GoldenBaselineHarness.GenerateTimestampNormalizedSha256ManifestForArchive(zipFilePath));
        Assert.NotEmpty(diskManifest);
        var archiveManifestPrefix = $"{GoldenBaselineHarness.EscapeManifestKeyPart(Path.GetFileName(zipFilePath))}::{GoldenBaselineHarness.EscapeManifestKeyPart(prodName)}/";
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
        var nativePathsByDocId = new Dictionary<string, string>(StringComparer.Ordinal);
        var parentNativePathsByDocId = new Dictionary<string, string>(StringComparer.Ordinal);
        var datDocIds = new HashSet<string>(StringComparer.Ordinal);
        var datNativePaths = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < datLines.Count; i++)
        {
            var cols = DelimitedTestParser.ParseLine(datLines[i], colDelim, quoteDelim).ToArray();
            Assert.Equal(header.Length, cols.Length);
            Assert.False(string.IsNullOrEmpty(cols[docIdIdx]), $"DAT row {i} has empty DOCID.");
            Assert.True(datDocIds.Add(cols[docIdIdx]), $"DAT row {i} repeats DOCID '{cols[docIdIdx]}'.");

            Assert.False(string.IsNullOrEmpty(cols[nativeIdx]), $"DAT row {i} has empty NATIVE_PATH.");
            Assert.True(datNativePaths.Add(cols[nativeIdx].Replace('\\', '/')), $"DAT row {i} repeats NATIVE_PATH '{cols[nativeIdx]}'.");
            Assert.Equal(cols[docIdIdx], Path.GetFileNameWithoutExtension(cols[nativeIdx].Replace('\\', '/')));
            AssertResolves(cols[nativeIdx], "native", rootPrefix, diskFiles, archiveEntryNames);
            Assert.False(string.IsNullOrEmpty(cols[imageIdx]), $"DAT row {i} has empty IMAGE_PATH.");
            var imageRel = cols[imageIdx].Replace('\\', '/');
            var imageDeliveryPath = $"{rootPrefix}{imageRel}";
            if (!diskFiles.Contains(imageDeliveryPath))
            {
                // Multi-page TIFF generator emits _001.tif, which ProductionSetPostValidator accepts as page-one fallback.
                var pageOneRel = Path.ChangeExtension(imageRel, null) + "_001" + Path.GetExtension(imageRel);
                AssertResolves(pageOneRel, "image (page-one fallback)", rootPrefix, diskFiles, archiveEntryNames);
            }
            else
            {
                AssertResolves(cols[imageIdx], "image", rootPrefix, diskFiles, archiveEntryNames);
            }
            Assert.False(string.IsNullOrEmpty(cols[textIdx]), $"DAT row {i} has no Extracted Text reference.");
            Assert.Equal(cols[docIdIdx], Path.GetFileNameWithoutExtension(cols[textIdx].Replace('\\', '/')));
            AssertResolves(cols[textIdx], "text", rootPrefix, diskFiles, archiveEntryNames);

            var normalizedNativePath = cols[nativeIdx].Replace('\\', '/');
            nativePathsByDocId.Add(cols[docIdIdx], normalizedNativePath);
            if (string.IsNullOrEmpty(cols[parentDocIdIdx]))
            {
                parentNativePathsByDocId.Add(cols[docIdIdx], normalizedNativePath);
            }

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

            // Verify that each attachment native stays in its parent's volume.
            Assert.True(
                nativePathsByDocId.TryGetValue(child.DocId, out var childNativePath),
                $"Missing child '{child.DocId}' native path.");
            Assert.True(
                nativePathsByDocId.TryGetValue(child.ParentDocId, out var parentNativePath),
                $"Missing parent '{child.ParentDocId}' native path for child '{child.DocId}'.");

            var childVolume = childNativePath.Split('/')[^2];
            var parentVolume = parentNativePath.Split('/')[^2];
            Assert.Equal(parentVolume, childVolume);
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

            if (request.LoadFile.AttachmentRate >= 100)
            {
                var childrenByParent = familyRecords
                    .Where(r => !string.IsNullOrEmpty(r.ParentDocId))
                    .GroupBy(r => r.ParentDocId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

                for (int i = 0; i < (int)request.Output.FileCount; i++)
                {
                    if (string.Equals(request.Output.ResolveFileType(i + 1), "eml", StringComparison.OrdinalIgnoreCase))
                    {
                        var expectedEmlParentDocId = request.Bates is not null
                            ? BatesSequence.FromConfig(request.Bates).Format(i).ToString()
                            : $"DOC{i + 1:D8}";
                        Assert.True(
                            childrenByParent.ContainsKey(expectedEmlParentDocId),
                            $"Expected EML parent '{expectedEmlParentDocId}' has no family children in 100% attachment recipe.");
                    }
                }
            }
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
        Assert.Equal((long)request.Output.FileCount, parentNativeFileCount);
        Assert.Equal((long)familyChildren.Count, attachmentNativeFileCount);

        // Actual Native Files on disk under NATIVES/; DAT references must name those files exactly.
        var actualNativeFiles = Directory.GetFiles(Path.Combine(productionDirectory, "NATIVES"), "*", SearchOption.AllDirectories);
        Assert.Equal(nativeFileCount, (long)actualNativeFiles.Length);
        var actualNativePaths = actualNativeFiles
            .Select(f => Path.GetRelativePath(productionDirectory, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var referencedNativePaths = datNativePaths
            .Select(p => p.StartsWith(rootPrefix, StringComparison.Ordinal) ? p[rootPrefix.Length..] : p)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(
            referencedNativePaths.SetEquals(actualNativePaths),
            $"DAT NATIVE_PATH values do not match the NATIVES/ inventory. Missing from DAT: [{string.Join(", ", actualNativePaths.Except(referencedNativePaths))}]. Extra in DAT: [{string.Join(", ", referencedNativePaths.Except(actualNativePaths))}].");

        // Actual volume subdirectories.
        var actualVolumeDirs = Directory.GetDirectories(Path.Combine(productionDirectory, "NATIVES"), "VOL*");
        Assert.Equal(volumeCount, actualVolumeDirs.Length);

        // Parent rows must split across volumes by the requested VolumeSize; attachment rows are excluded.
        for (int i = 0; i < (int)request.Output.FileCount; i++)
        {
            var expectedDocId = request.Bates is not null
                ? BatesSequence.FromConfig(request.Bates).Format(i).ToString()
                : $"DOC{i + 1:D8}";
            var expectedVolume = $"VOL{(i / request.Production.VolumeSize) + 1:D3}";
            Assert.True(
                parentNativePathsByDocId.TryGetValue(expectedDocId, out var nativePath),
                $"Missing expected parent DOCID '{expectedDocId}' in DAT.");
            var actualVolume = nativePath.Split('/')[^2];
            Assert.Equal(expectedVolume, actualVolume);
        }

        long totalDocuments = datLines.Count - 1;
        long parentDocuments = totalDocuments - familyChildren.Count;
        Assert.Equal((long)request.Output.FileCount, parentDocuments);

        // CheckedLoadFileRowCounts["dat"] includes its header row (see ProductionSetPostValidator),
        // so it is one more than the data record count.
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
        ITestOutputHelper? output = null,
        IReadOnlyDictionary<string, string>? expectedContentHashesByMember = null,
        IReadOnlyDictionary<string, StandardDeliveryRow>? expectedRowsByControlNumber = null)
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

        // Golden hash-manifest tooling on the archive, checked against independent stream hashes below.
        var archiveManifest = ParseManifest(GoldenBaselineHarness.GenerateTimestampNormalizedSha256ManifestForArchive(zipFilePath));
        Assert.NotEmpty(archiveManifest);

        // Independent reader matches ZIP inventory and streaming content hashes to disk artifacts.
        var tempExtractDir = Path.Combine(Path.GetDirectoryName(zipFilePath)!, "extracted_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempExtractDir);
        try
        {
            ZipFile.ExtractToDirectory(zipFilePath, tempExtractDir);
            var diskFiles = Directory.GetFiles(tempExtractDir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(tempExtractDir, f).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);

            using (var sharpZip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipFilePath))
            {
                var sharpZipEntries = new HashSet<string>(StringComparer.Ordinal);
                var verifiedExpectedMembers = new HashSet<string>(StringComparer.Ordinal);
                foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in sharpZip)
                {
                    if (!entry.IsDirectory && !entry.Name.EndsWith('/'))
                    {
                        sharpZipEntries.Add(entry.Name);
                        var diskPath = Path.Combine(tempExtractDir, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                        Assert.True(File.Exists(diskPath), $"Matching disk file not found for archive entry: {entry.Name}");

                        using var entryStream = sharpZip.GetInputStream(entry);
                        using var diskStream = File.OpenRead(diskPath);
                        Assert.Equal(diskStream.Length, entry.Size);

                        var entryHash = System.Security.Cryptography.SHA256.HashData(entryStream);
                        var entryHashHex = Convert.ToHexString(entryHash).ToLowerInvariant();
                        var diskHash = System.Security.Cryptography.SHA256.HashData(diskStream);
                        Assert.True(
                            diskHash.AsSpan().SequenceEqual(entryHash),
                            $"Streaming hash mismatch for '{entry.Name}' against disk artifact.");

                        if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            var archiveName = GoldenBaselineHarness.EscapeManifestKeyPart(Path.GetFileName(zipFilePath));
                            var manifestKey = $"{archiveName}::{GoldenBaselineHarness.EscapeManifestKeyPart(entry.Name)}";
                            Assert.True(
                                archiveManifest.TryGetValue(manifestKey, out var manifestHash),
                                $"Archive manifest is missing '{entry.Name}'.");
                            Assert.Equal(manifestHash, entryHashHex);
                        }

                        if (expectedContentHashesByMember is not null && expectedContentHashesByMember.TryGetValue(entry.Name, out var expectedHash))
                        {
                            Assert.True(
                                string.Equals(expectedHash, entryHashHex, StringComparison.Ordinal),
                                $"Pre-packaging hash mismatch for '{entry.Name}': expected {expectedHash}, got {entryHashHex}.");
                            verifiedExpectedMembers.Add(entry.Name);
                        }
                    }
                }
                Assert.True(diskFiles.SetEquals(sharpZipEntries));
                if (expectedContentHashesByMember is not null)
                {
                    Assert.True(
                        verifiedExpectedMembers.SetEquals(expectedContentHashesByMember.Keys),
                        $"Expected pre-packaging members missing from ZIP: [{string.Join(", ", expectedContentHashesByMember.Keys.Except(verifiedExpectedMembers))}].");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempExtractDir))
            {
                Directory.Delete(tempExtractDir, true);
            }
        }

        // Locate included DAT load file in archive root.
        var datEntry = archive.Entries.FirstOrDefault(e =>
            !e.FullName.Contains('/') &&
            e.FullName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.EndsWith(".concordance.dat", StringComparison.OrdinalIgnoreCase));
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
        var textPaths = new HashSet<string>(StringComparer.Ordinal);
        var controlNumbers = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < datLines.Length; i++)
        {
            var cols = DelimitedTestParser.ParseLine(datLines[i], colDelim, quoteDelim).ToArray();
            Assert.Equal(header.Length, cols.Length);
            Assert.False(string.IsNullOrWhiteSpace(cols[controlNumIdx]), $"Standard DAT row {i} has a blank Control Number.");
            Assert.True(controlNumbers.Add(cols[controlNumIdx]), $"Standard DAT row {i} repeats Control Number '{cols[controlNumIdx]}'.");

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

            // Extracted Text is enabled for this recipe, so every row must reference its expected text sidecar.
            var textRel = cols[extractedTextIdx].Replace('\\', '/');
            Assert.False(string.IsNullOrEmpty(textRel), $"Standard DAT row {i} has no Extracted Text reference.");
            Assert.True(textRel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase), $"Standard DAT row {i} Extracted Text must end with .txt: {textRel}");
            Assert.NotEqual(nativeRel, textRel);
            // A non-.txt parent's sidecar replaces the native extension. A .txt native cannot
            // use that rule (ChangeExtension returns the native path), so only require a
            // distinct, resolving sidecar for it.
            if (string.IsNullOrEmpty(cols[parentDocIdIdx]) &&
                !nativeRel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var expectedTextRel = Path.ChangeExtension(nativeRel, ".txt").Replace('\\', '/');
                Assert.Equal(expectedTextRel, textRel);
            }
            Assert.Contains(textRel, archiveEntryNames);
            textPaths.Add(textRel);

            if (expectedRowsByControlNumber is not null)
            {
                Assert.True(
                    expectedRowsByControlNumber.TryGetValue(cols[controlNumIdx], out var expectedRow),
                    $"Standard DAT Control Number '{cols[controlNumIdx]}' is absent from the recipe inventory.");
                Assert.Equal(expectedRow!.NativePath, nativeRel);
                Assert.Equal(expectedRow.TextPath, textRel);
                Assert.Equal(expectedRow.ParentDocId, cols[parentDocIdIdx]);
            }

            familyRecords.Add(new FamilyRecord(
                cols[controlNumIdx],
                cols[begAttachIdx],
                cols[endAttachIdx],
                cols[parentDocIdIdx]));
        }

        if (expectedRowsByControlNumber is not null)
        {
            Assert.Equal(expectedRowsByControlNumber.Count, familyRecords.Count);
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

        // Derive expected root artifacts from requested formats, not archive contents.
        var formats = request.LoadFile.Formats.Count > 0
            ? request.LoadFile.Formats
            : new List<LoadFileFormat> { LoadFileFormat.Dat };
        var baseStem = Path.GetFileNameWithoutExtension(datEntry.FullName);

        // Locate included OPT load file if requested, and verify format structure and image resolution.
        bool optRequested = formats.Contains(LoadFileFormat.Opt);
        var optEntry = archive.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".opt", StringComparison.OrdinalIgnoreCase));
        if (optRequested)
        {
            Assert.NotNull(optEntry);
            using var optReader = new StreamReader(optEntry!.Open(), Encoding.UTF8);
            var optContent = await optReader.ReadToEndAsync();
            var optLines = optContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.NotEmpty(optLines);

            foreach (var optLine in optLines)
            {
                var parts = optLine.Split(',');
                Assert.True(parts.Length >= 7, $"Standard OPT line had {parts.Length} fields, expected >= 7: {optLine}");
                var imageRel = parts[2].Trim().Replace('\\', '/');
                Assert.Contains(imageRel, archiveEntryNames);
            }
        }
        else
        {
            Assert.Null(optEntry);
        }

        // Compare DAT native references against the independently established recipe inventory.
        // Output.FileCount is the requested original-document count; attachments add rows.
        long expectedNativeCount = request.Output.FileCount + familyChildren.Count;
        Assert.Equal(expectedNativeCount, (long)nativePaths.Count);
        long parentDocuments = (long)nativePaths.Count - familyChildren.Count;
        Assert.Equal((long)request.Output.FileCount, parentDocuments);
        Assert.DoesNotContain(textPaths, p => nativePaths.Contains(p));

        AssertNativeDeliveryTypeCoverage(nativePaths);
        Assert.Contains(textPaths, n => n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

        static bool IsArchiveLoadFileArtifact(string name)
        {
            if (name.Contains('/'))
            {
                return false;
            }

            return name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".opt", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        }

        bool disambiguateConcordance = formats.Contains(LoadFileFormat.Dat) && formats.Contains(LoadFileFormat.Concordance);
        var expectedRootArtifacts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var format in formats)
        {
            var ext = format switch
            {
                LoadFileFormat.Dat => ".dat",
                LoadFileFormat.Csv => ".csv",
                LoadFileFormat.Opt => ".opt",
                LoadFileFormat.Concordance => disambiguateConcordance ? ".concordance.dat" : ".dat",
                LoadFileFormat.EdrmXml => ".xml",
                _ => ".dat",
            };
            var fileName = baseStem + ext;
            expectedRootArtifacts.Add(fileName);
            expectedRootArtifacts.Add(fileName + "_properties.json");
        }
        Assert.Contains(datEntry.FullName + "_properties.json", archiveEntryNames);
        var actualRootArtifacts = archiveEntryNames.Where(name => !name.Contains('/')).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            actualRootArtifacts.SetEquals(expectedRootArtifacts),
            $"Unexpected root artifacts in Standard ZIP: [{string.Join(", ", actualRootArtifacts.Except(expectedRootArtifacts))}]; missing: [{string.Join(", ", expectedRootArtifacts.Except(actualRootArtifacts))}].");

        var deliveredArchiveNatives = archiveEntryNames
            .Where(name => !IsArchiveLoadFileArtifact(name) && !textPaths.Contains(name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            nativePaths.SetEquals(deliveredArchiveNatives),
            $"Standard DAT native references do not match delivered archive natives. Missing from DAT: [{string.Join(", ", deliveredArchiveNatives.Except(nativePaths))}]. Extra in DAT: [{string.Join(", ", nativePaths.Except(deliveredArchiveNatives))}].");

        // Count Native Files by their DAT role, so a .txt attachment still counts as a Native.
        long nativeFilesCount = nativePaths.Count;

        long totalDocuments = datLines.Length - 1;
        Assert.Equal(expectedNativeCount, totalDocuments);
        Assert.Equal((long)request.Output.FileCount, totalDocuments - familyChildren.Count);

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

    private static void AssertNativeDeliveryTypeCoverage(IEnumerable<string> nativeFilePaths)
    {
        var names = nativeFilePaths.ToList();
        Assert.Contains(names, n => n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith(".eml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertFamilyRanges(IReadOnlyList<FamilyRecord> familyRecords)
    {
        foreach (var row in familyRecords.Where(r =>
            string.IsNullOrEmpty(r.ParentDocId) && string.Equals(r.BegAttach, r.EndAttach, StringComparison.Ordinal)))
        {
            Assert.Equal(row.DocId, row.BegAttach);
            Assert.Equal(row.DocId, row.EndAttach);
        }

        var parents = familyRecords
            .Where(r => string.IsNullOrEmpty(r.ParentDocId) && !string.Equals(r.BegAttach, r.EndAttach, StringComparison.Ordinal))
            .ToDictionary(r => r.DocId, StringComparer.Ordinal);
        var childrenByParent = familyRecords
            .Where(r => !string.IsNullOrEmpty(r.ParentDocId))
            .GroupBy(r => r.ParentDocId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (parentId, group) in childrenByParent)
        {
            Assert.True(parents.TryGetValue(parentId, out var parent), $"Family child parent '{parentId}' has no parent row.");
            Assert.Equal(parent!.DocId, parent.BegAttach);
            foreach (var child in group)
            {
                Assert.Equal(parent.BegAttach, child.BegAttach);
                Assert.Equal(parent.EndAttach, child.EndAttach);
                Assert.True(
                    string.Compare(child.DocId, parent.BegAttach, StringComparison.Ordinal) > 0,
                    $"Child doc id '{child.DocId}' must sort after parent BegAttach '{parent.BegAttach}'.");
                Assert.True(
                    string.Compare(child.DocId, parent.EndAttach, StringComparison.Ordinal) <= 0,
                    $"Child doc id '{child.DocId}' must sort no later than parent EndAttach '{parent.EndAttach}'.");
            }
            var lastChild = group.OrderBy(r => r.DocId, StringComparer.Ordinal).Last();
            Assert.Equal(lastChild.DocId, parent.EndAttach);
        }

        foreach (var (parentId, parent) in parents)
        {
            Assert.Equal(parent.DocId, parent.BegAttach);
            Assert.True(
                childrenByParent.TryGetValue(parentId, out var group),
                $"Parent '{parentId}' has BegAttach '{parent.BegAttach}' != EndAttach '{parent.EndAttach}', but has no family children.");
            Assert.NotEmpty(group);
            var lastChild = group.OrderBy(r => r.DocId, StringComparer.Ordinal).Last();
            Assert.Equal(lastChild.DocId, parent.EndAttach);
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
