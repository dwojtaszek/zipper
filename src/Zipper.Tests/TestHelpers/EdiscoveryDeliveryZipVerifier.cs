using System.IO.Compression;
using System.Security.Cryptography;
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

        // 1. Independent reader: verify ZIP inventory and streaming content hashes against disk
        using var archive = ZipFile.OpenRead(zipFilePath);
        Assert.NotEmpty(archive.Entries);

        var archiveFileEntries = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .ToList();

        var archiveEntryNames = archiveFileEntries
            .Select(e => e.FullName)
            .ToHashSet(StringComparer.Ordinal);

        var diskFiles = Directory.GetFiles(productionDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(parentDir, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        // Root prefix assertion
        foreach (var entry in archiveFileEntries)
        {
            Assert.StartsWith(rootPrefix, entry.FullName, StringComparison.Ordinal);
        }

        // Inventory assertion: every file on disk is an archive entry, and vice versa
        Assert.True(
            diskFiles.SetEquals(archiveEntryNames),
            $"Archive inventory mismatch. Missing in ZIP: [{string.Join(", ", diskFiles.Except(archiveEntryNames))}]. Extra in ZIP: [{string.Join(", ", archiveEntryNames.Except(diskFiles))}].");

        // Streaming content hash assertion: compare entry SHA-256 and CRC32 to disk
        byte[] buffer = new byte[8192];
        foreach (var entry in archiveFileEntries)
        {
            var diskPath = Path.Combine(parentDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(diskPath), $"Matching disk file not found for entry '{entry.FullName}'.");

            var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
            using var entrySha = SHA256.Create();
            using var entryStream = entry.Open();
            int bytesRead;
            long totalRead = 0;
            while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                entrySha.TransformBlock(buffer, 0, bytesRead, null, 0);
                crc.Update(new ArraySegment<byte>(buffer, 0, bytesRead));
                totalRead += bytesRead;
            }

            entrySha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            Assert.Equal(entry.Length, totalRead);
            Assert.Equal(entry.Crc32, (uint)crc.Value);
            var entryHash = Convert.ToHexString(entrySha.Hash!).ToLowerInvariant();

            using var diskSha = SHA256.Create();
            using var diskStream = File.OpenRead(diskPath);
            while ((bytesRead = diskStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                diskSha.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            diskSha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var diskHash = Convert.ToHexString(diskSha.Hash!).ToLowerInvariant();

            Assert.Equal(diskHash, entryHash);
        }

        // 2. Production Set validation pipeline: run PostGenerationValidator
        var validationReport = ProductionSetPostValidator.Validate(productionDirectory, request);
        Assert.Equal("passed", validationReport.Status);
        Assert.Equal(0, validationReport.ErrorCount);
        Assert.Equal(0, validationReport.WarningCount);
        Assert.Empty(validationReport.Findings);

        // 3. Golden hash-manifest tooling
        var diskManifest = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(productionDirectory);
        var archiveManifest = GoldenBaselineHarness.GenerateTimestampNormalizedSha256ManifestForArchive(zipFilePath);
        Assert.False(string.IsNullOrEmpty(diskManifest));
        Assert.False(string.IsNullOrEmpty(archiveManifest));
        Assert.Contains("_manifest.json", archiveManifest, StringComparison.Ordinal);
        Assert.Contains("DATA/loadfile.dat", archiveManifest, StringComparison.Ordinal);

        // 4. Assert DAT/OPT references resolve inside the delivery (both disk and ZIP)
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

        int familyParentCount = 0;
        int familyChildCount = 0;

        for (int i = 1; i < datLines.Count; i++)
        {
            var cols = DelimitedTestParser.ParseLine(datLines[i], colDelim, quoteDelim).ToArray();

            // Native path resolution
            if (!string.IsNullOrEmpty(cols[nativeIdx]))
            {
                var rel = cols[nativeIdx].Replace('\\', '/');
                var diskEntryKey = $"{rootPrefix}{rel}";
                Assert.True(diskFiles.Contains(diskEntryKey), $"DAT referenced native file missing on disk: {rel}");
                Assert.Contains(diskEntryKey, archiveEntryNames);
            }

            // Image path resolution
            if (!string.IsNullOrEmpty(cols[imageIdx]))
            {
                var rel = cols[imageIdx].Replace('\\', '/');
                var diskEntryKey = $"{rootPrefix}{rel}";
                Assert.True(diskFiles.Contains(diskEntryKey), $"DAT referenced image file missing on disk: {rel}");
                Assert.Contains(diskEntryKey, archiveEntryNames);
            }

            // Text path resolution
            if (textIdx >= 0 && textIdx < cols.Length && !string.IsNullOrEmpty(cols[textIdx]))
            {
                var rel = cols[textIdx].Replace('\\', '/');
                var diskEntryKey = $"{rootPrefix}{rel}";
                Assert.True(diskFiles.Contains(diskEntryKey), $"DAT referenced text file missing on disk: {rel}");
                Assert.Contains(diskEntryKey, archiveEntryNames);
            }

            // Family tracking
            if (begAttachIdx >= 0 && endAttachIdx >= 0 && parentDocIdIdx >= 0)
            {
                string begAttach = cols[begAttachIdx];
                string endAttach = cols[endAttachIdx];
                string parentDocId = cols[parentDocIdIdx];

                if (string.IsNullOrEmpty(parentDocId) && !string.Equals(begAttach, endAttach, StringComparison.Ordinal))
                {
                    familyParentCount++;
                }
                else if (!string.IsNullOrEmpty(parentDocId))
                {
                    familyChildCount++;
                    Assert.Equal(begAttach, parentDocId);
                    Assert.Contains("_A", cols[docIdIdx], StringComparison.Ordinal);
                }
            }
        }

        // OPT resolution
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
            var imgRel = parts[2].Trim().Replace('\\', '/');
            var diskEntryKey = $"{rootPrefix}{imgRel}";
            Assert.True(diskFiles.Contains(diskEntryKey), $"OPT referenced image missing on disk: {imgRel}");
            Assert.Contains(diskEntryKey, archiveEntryNames);
        }

        // 5. Family membership survival check
        if (request.Metadata.WithFamilies && request.Output.HasFileType("eml") && request.LoadFile.AttachmentRate > 0)
        {
            Assert.True(familyParentCount > 0, "Expected family parent records in DAT load file.");
            Assert.True(familyChildCount > 0, "Expected family child attachment records in DAT load file.");
        }

        // 6. Manifest and validation counts agree
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

        // Actual native files on disk under NATIVES/
        var actualNativeFiles = Directory.GetFiles(Path.Combine(productionDirectory, "NATIVES"), "*", SearchOption.AllDirectories);
        Assert.Equal(nativeFileCount, (long)actualNativeFiles.Length);

        // Actual volume subdirectories
        var actualVolumeDirs = Directory.GetDirectories(Path.Combine(productionDirectory, "NATIVES"), "VOL*");
        Assert.Equal(volumeCount, actualVolumeDirs.Length);

        long totalDocuments = datLines.Count - 1;
        Assert.Equal((int)totalDocuments + 1, validationReport.CheckedLoadFileRowCounts["dat"]);
        Assert.Equal(optLines.Count, validationReport.CheckedLoadFileRowCounts["opt"]);

        // 7. Report documents, Native Files, and ZIP members separately
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

        using var archive = ZipFile.OpenRead(zipFilePath);
        Assert.NotEmpty(archive.Entries);

        byte[] buffer = new byte[8192];
        var archiveEntryNames = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToHashSet(StringComparer.Ordinal);

        // Streaming CRC & decompression verification for every entry
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
            using var stream = entry.Open();
            int bytesRead;
            long totalRead = 0;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc.Update(new ArraySegment<byte>(buffer, 0, bytesRead));
                totalRead += bytesRead;
            }

            Assert.Equal(entry.Length, totalRead);
            Assert.Equal(entry.Crc32, (uint)crc.Value);
        }

        // Golden hash-manifest tooling on archive
        var archiveManifest = GoldenBaselineHarness.GenerateTimestampNormalizedSha256ManifestForArchive(zipFilePath);
        Assert.False(string.IsNullOrEmpty(archiveManifest));

        // Locate included DAT load file in archive root
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
        int extractedTextIdx = Array.FindIndex(header, h => string.Equals(h, "Extracted Text", StringComparison.OrdinalIgnoreCase));
        int begAttachIdx = Array.FindIndex(header, h => string.Equals(h, "BEGATTACH", StringComparison.OrdinalIgnoreCase));
        int endAttachIdx = Array.FindIndex(header, h => string.Equals(h, "ENDATTACH", StringComparison.OrdinalIgnoreCase));
        int parentDocIdIdx = Array.FindIndex(header, h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase));
        int controlNumIdx = Array.FindIndex(header, h => string.Equals(h, "Control Number", StringComparison.OrdinalIgnoreCase));

        Assert.True(filePathIdx >= 0, "File Path column missing from Standard DAT header.");
        Assert.True(controlNumIdx >= 0, "Control Number column missing from Standard DAT header.");

        int familyParentCount = 0;
        int familyChildCount = 0;

        for (int i = 1; i < datLines.Length; i++)
        {
            var cols = DelimitedTestParser.ParseLine(datLines[i], colDelim, quoteDelim).ToArray();

            // Native file path resolves inside ZIP
            var nativeRel = cols[filePathIdx].Replace('\\', '/');
            Assert.Contains(nativeRel, archiveEntryNames);

            // Extracted text resolves inside ZIP when enabled
            if (extractedTextIdx >= 0 && extractedTextIdx < cols.Length && !string.IsNullOrEmpty(cols[extractedTextIdx]))
            {
                var textRel = cols[extractedTextIdx].Replace('\\', '/');
                Assert.Contains(textRel, archiveEntryNames);
            }

            // Family tracking
            if (begAttachIdx >= 0 && endAttachIdx >= 0 && parentDocIdIdx >= 0)
            {
                string begAttach = cols[begAttachIdx];
                string endAttach = cols[endAttachIdx];
                string parentDocId = cols[parentDocIdIdx];

                if (string.IsNullOrEmpty(parentDocId) && !string.Equals(begAttach, endAttach, StringComparison.Ordinal))
                {
                    familyParentCount++;
                }
                else if (!string.IsNullOrEmpty(parentDocId))
                {
                    familyChildCount++;
                    Assert.Equal(begAttach, parentDocId);
                    Assert.Contains("_A", cols[controlNumIdx], StringComparison.Ordinal);
                }
            }
        }

        // Family membership survival check
        if (request.Metadata.WithFamilies && request.Output.HasFileType("eml") && request.LoadFile.AttachmentRate > 0)
        {
            Assert.True(familyParentCount > 0, "Expected family parent records in Standard DAT.");
            Assert.True(familyChildCount > 0, "Expected family child attachment records in Standard DAT.");
        }

        // Count Native Files (excluding load files, properties sidecars, and extracted text files)
        long nativeFilesCount = archive.Entries.Count(e =>
            !e.FullName.EndsWith('/') &&
            !e.FullName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.EndsWith(".opt", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

        long totalDocuments = datLines.Length - 1;

        // 7. Report documents, Native Files, and ZIP members separately
        var report = new EdiscoveryDeliveryReport(
            DocumentCount: totalDocuments,
            NativeFileCount: nativeFilesCount,
            ZipMemberCount: archive.Entries.Count,
            VolumeCount: 1);

        output?.WriteLine($"[E-Discovery Delivery Report] {report}");
        return report;
    }
}
