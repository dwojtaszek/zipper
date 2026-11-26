using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;
using Zipper.LoadFiles;
using Zipper.Tests.TestHelpers;

namespace Zipper.Tests;

/// <summary>
/// End-to-end round-trip ZIP generation verification for e-discovery deliveries (#1464).
/// Tests Standard mode with --include-load-file and Production Set mode with --production-zip,
/// multi-volume splitting, mixed file types (PDF, Office, Email with Attachments, TIFF, Extracted Text),
/// independent reader streaming content hash matching, and opt-in 300- and 5,000-document scale recipes.
/// </summary>
[Collection("ConsoleTests")]
public class EdiscoveryDeliveryZipRoundTripTests : WorkingDirectoryTestBase
{
    private readonly ITestOutputHelper output;

    public EdiscoveryDeliveryZipRoundTripTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static bool ScaleRecipesEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("ZIPPER_SCALE_RECIPES"), "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("ZIPPER_SCALE_RECIPES"), "true", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task StandardMode_WithIncludeLoadFile_RoundTripZipVerification()
    {
        var testDir = Path.Combine(this.TempDir, $"std_rt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var args = new[]
            {
                "--types", "pdf:1,docx:1,eml:1,tiff:1",
                "--count", "4",
                "--output-path", testDir,
                "--include-load-file",
                "--loadfile-format", "dat",
                "--with-families",
                "--attachment-rate", "100",
                "--with-text",
                "--seed", "42",
            };

            var request = Cli.Pipeline.Build(args);
            Assert.NotNull(request);

            var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));
            Assert.True(exitCode == 0, $"Program.Main failed with exit code {exitCode}. Stderr: {stderr}, Stdout: {stdout}");

            var zipFiles = Directory.GetFiles(testDir, "*.zip");
            var zipFile = Assert.Single(zipFiles);

            var expected = BuildStandardExpectations(request!);
            var report = await EdiscoveryDeliveryZipVerifier.VerifyStandardModeZipDeliveryAsync(
                zipFile, request!, this.output, expected.Hashes, expected.Rows);

            Assert.True(report.DocumentCount > 0, "Document count must be greater than zero.");
            Assert.True(report.NativeFileCount > 0, "Native file count must be greater than zero.");
            Assert.True(report.ZipMemberCount > 0, "ZIP member count must be greater than zero.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Fact]
    public async Task ProductionSetMode_WithProductionZip_MultiVolume_RoundTripZipVerification()
    {
        var testDir = Path.Combine(this.TempDir, $"prod_rt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var report = await RunProductionSetScaleRecipeAsync(4, 2, testDir, this.output);

            Assert.True(report.VolumeCount >= 2, $"Expected at least 2 volumes, got {report.VolumeCount}.");
            Assert.True(report.DocumentCount > 0, "Document count must be greater than zero.");
            Assert.True(report.NativeFileCount > 0, "Native file count must be greater than zero.");
            Assert.True(report.ZipMemberCount > 0, "ZIP member count must be greater than zero.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [SkippableFact]
    [Trait("Category", "Scale")]
    public async Task ProductionSetMode_ScaleRecipe_300Documents()
    {
        Skip.If(!ScaleRecipesEnabled, "Scale recipes are opt-in. Set ZIPPER_SCALE_RECIPES=1 to run.");

        var testDir = Path.Combine(this.TempDir, $"scale300_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var report = await RunProductionSetScaleRecipeAsync(300, 100, testDir, this.output);

            Assert.True(report.VolumeCount >= 3, $"Expected at least 3 volumes for 300 docs with volume size 100, got {report.VolumeCount}.");
            Assert.True(report.DocumentCount >= 300, "Document count must be >= 300.");
            Assert.True(report.NativeFileCount >= 300, "Native file count must be >= 300.");
            Assert.True(report.ZipMemberCount > 300, "ZIP member count must exceed native file count.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [SkippableFact]
    [Trait("Category", "Scale")]
    public async Task ProductionSetMode_ScaleRecipe_5000Documents()
    {
        Skip.If(!ScaleRecipesEnabled, "Scale recipes are opt-in. Set ZIPPER_SCALE_RECIPES=1 to run.");

        var testDir = Path.Combine(this.TempDir, $"scale5000_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var report = await RunProductionSetScaleRecipeAsync(5000, 1000, testDir, this.output);

            Assert.True(report.VolumeCount >= 5, $"Expected at least 5 volumes for 5000 docs with volume size 1000, got {report.VolumeCount}.");
            Assert.True(report.DocumentCount >= 5000, "Document count must be >= 5000.");
            Assert.True(report.NativeFileCount >= 5000, "Native file count must be >= 5000.");
            Assert.True(report.ZipMemberCount > 5000, "ZIP member count must exceed native file count.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Fact]
    public async Task StandardMode_TamperedNativeBytesWithValidCrc_FailsVerification()
    {
        var testDir = Path.Combine(this.TempDir, $"std_tamper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var args = new[]
            {
                "--types", "pdf:1,docx:1,eml:1,tiff:1",
                "--count", "4",
                "--output-path", testDir,
                "--include-load-file",
                "--loadfile-format", "dat",
                "--with-families",
                "--attachment-rate", "100",
                "--with-text",
                "--seed", "42",
            };

            var request = Cli.Pipeline.Build(args);
            Assert.NotNull(request);

            var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));
            Assert.True(exitCode == 0, $"Program.Main failed with exit code {exitCode}. Stderr: {stderr}, Stdout: {stdout}");

            var zipFile = Assert.Single(Directory.GetFiles(testDir, "*.zip"));
            var expected = BuildStandardExpectations(request!);
            var pdfMember = Assert.Single(expected.Hashes.Keys, k => k.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            var tamperedZip = Path.Combine(testDir, "tampered.zip");

            // The intact archive passes the pre-packaging hash inventory.
            await EdiscoveryDeliveryZipVerifier.VerifyStandardModeZipDeliveryAsync(
                zipFile, request!, null, expected.Hashes, expected.Rows);

            int mutatedEntryCount = 0;
            using (var source = ZipFile.OpenRead(zipFile))
            using (var target = ZipFile.Open(tamperedZip, ZipArchiveMode.Create))
            {
                Assert.Single(source.Entries, e => string.Equals(e.FullName, pdfMember, StringComparison.Ordinal));
                foreach (var entry in source.Entries)
                {
                    byte[] bytes;
                    using (var reader = new BinaryReader(entry.Open()))
                    {
                        bytes = reader.ReadBytes((int)entry.Length);
                    }

                    if (string.Equals(entry.FullName, pdfMember, StringComparison.Ordinal))
                    {
                        Assert.NotEmpty(bytes);
                        bytes[0] ^= 0xFF;
                        mutatedEntryCount++;
                    }

                    var copiedEntry = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                    using var writer = copiedEntry.Open();
                    writer.Write(bytes);
                }
            }

            Assert.Equal(1, mutatedEntryCount);
            ZipAssert.AssertValidZip(tamperedZip);
            var exception = await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() =>
                EdiscoveryDeliveryZipVerifier.VerifyStandardModeZipDeliveryAsync(
                    tamperedZip, request!, null, expected.Hashes, expected.Rows));
            Assert.Contains(pdfMember, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    private sealed record StandardExpectations(
        Dictionary<string, string> Hashes,
        Dictionary<string, StandardDeliveryRow> Rows);

    private static StandardExpectations BuildStandardExpectations(FileGenerationRequest request)
    {
        var generators = FileGeneratorFactory.CreateMap(request);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new Dictionary<string, StandardDeliveryRow>(StringComparer.Ordinal);
        var fileCount = request.Output.FileCount;

        for (long i = 1; i <= fileCount; i++)
        {
            var fileType = request.Output.ResolveFileType(i);
            var folderNumber = Distributions.GetFolderNumber(i, fileCount, request.Output.Folders, request.LoadFile.Distribution);
            var folderPrefix = $"folder_{folderNumber:D3}/";
            var fileName = $"{i:D8}.{fileType}";
            var workItem = new FileWorkItem
            {
                Index = i,
                FolderNumber = folderNumber,
                FolderName = $"folder_{folderNumber:D3}",
                FileName = fileName,
                FilePathInZip = $"folder_{folderNumber:D3}/{fileName}",
                FileType = fileType,
            };

            var generated = generators[fileType].Generate(workItem, request);
            AddExpectedHash(hashes, workItem.FilePathInZip, generated.Content);
            var textContent = string.Equals(fileType, "eml", StringComparison.OrdinalIgnoreCase)
                ? PlaceholderFiles.EmlExtractedText
                : PlaceholderFiles.ExtractedText;
            var textPath = $"{folderPrefix}{TextPathHelper.GetTextPath(fileName)}";
            AddExpectedHash(hashes, textPath, textContent);

            var controlNumber = $"DOC{i:D8}";
            AddExpectedRow(rows, controlNumber, workItem.FilePathInZip, textPath);

            var attachments = generated.GetAttachments();
            var attachmentNames = attachments.Select(a => a.filename).ToList();
            for (int attachmentIndex = 0; attachmentIndex < attachments.Count; attachmentIndex++)
            {
                var attachmentNumber = attachmentIndex + 1;
                var attachment = attachments[attachmentIndex];
                var isDuplicate = FamilyPlan.IsDuplicateAttachmentName(attachmentNames, attachmentIndex);
                var attachmentPath = FamilyPlan.GetAttachmentPath(workItem, attachment.filename, attachmentNumber, isDuplicate);
                var attachmentTextPath = FamilyPlan.GetAttachmentTextPath(workItem, attachment.filename, attachmentNumber);
                AddExpectedHash(hashes, attachmentPath, attachment.content);
                AddExpectedHash(hashes, attachmentTextPath, PlaceholderFiles.ExtractedText);
                AddExpectedRow(
                    rows,
                    FamilyPlan.GetChildId(controlNumber, attachmentNumber),
                    attachmentPath,
                    attachmentTextPath);
            }
        }

        return new StandardExpectations(hashes, rows);
    }

    private static void AddExpectedHash(Dictionary<string, string> expected, string memberName, byte[] content)
    {
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        Assert.True(expected.TryAdd(memberName.Replace('\\', '/'), hash), $"Duplicate expected ZIP member '{memberName}'.");
    }

    private static void AddExpectedRow(
        Dictionary<string, StandardDeliveryRow> expected,
        string controlNumber,
        string nativePath,
        string textPath)
    {
        Assert.True(
            expected.TryAdd(controlNumber, new StandardDeliveryRow(nativePath.Replace('\\', '/'), textPath.Replace('\\', '/'))),
            $"Duplicate expected Control Number '{controlNumber}'.");
    }

    public static async Task<EdiscoveryDeliveryReport> RunProductionSetScaleRecipeAsync(
        int documentCount,
        int volumeSize,
        string outputDirectory,
        ITestOutputHelper? output = null)
    {
        var args = new[]
        {
            "--production-set",
            "--production-zip",
            "--types", "pdf:1,docx:1,eml:1,tiff:1",
            "--count", documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--output-path", outputDirectory,
            "--volume-size", volumeSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--bates-prefix", "PROD",
            "--with-families",
            "--attachment-rate", "100",
            "--with-text",
            "--seed", "42",
        };

        var request = Cli.Pipeline.Build(args);
        Assert.NotNull(request);

        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));
        Assert.True(exitCode == 0, $"Program.Main failed with exit code {exitCode}. Stderr: {stderr}, Stdout: {stdout}");

        var prodDirs = Directory.GetDirectories(outputDirectory, "PRODUCTION_*");
        var prodDir = Assert.Single(prodDirs);

        var zipFiles = Directory.GetFiles(outputDirectory, "PRODUCTION_*.zip");
        var zipFile = Assert.Single(zipFiles);

        return await EdiscoveryDeliveryZipVerifier.VerifyProductionSetZipDeliveryAsync(zipFile, prodDir, request!, output);
    }
}
