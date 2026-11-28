using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Zipper.Tests.TestHelpers;
using IndependentZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

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
    public async Task NestedArchiveDeliveries_PreserveOpaquePayloadsInBothOrders()
    {
        var destination = Path.Combine(this.TempDir, "nested");
        await RunDeliveryAsync(
            "--archive-test-suite", "compatibility",
            "--archive-test-cases", "nested-archives-depth-two,nested-opaque-forward,nested-opaque-reversed",
            "--seed", "42", "--output-path", destination);

        var archives = Directory.GetFiles(destination, "*.json").ToDictionary(path =>
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            return json.RootElement.GetProperty("caseKey").GetString()!;
        }, path => Path.ChangeExtension(path, ".zip"), StringComparer.Ordinal);

        using var control = new IndependentZipFile(archives["nested-archives-depth-two"]);
        using var inner = control.GetInputStream(control.GetEntry("inner.zip"));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["inner.zip"] = Convert.ToHexString(SHA256.HashData(inner)),
            ["zip-looking.bin"] = Convert.ToHexString(SHA256.HashData("PK\u0003\u0004ordinary bytes"u8)),
        };

        VerifyOpaqueDelivery(archives["nested-opaque-forward"], "forward", ["inner.zip", "zip-looking.bin"], expected);
        VerifyOpaqueDelivery(archives["nested-opaque-reversed"], "reversed", ["zip-looking.bin", "inner.zip"], expected);
    }

    [Fact]
    public async Task StandardArchive_PreservesOfficeNativeFilesAsOpaquePayloads()
    {
        var destination = Path.Combine(this.TempDir, "office");
        Directory.CreateDirectory(destination);
        string[] args = ["--types", "docx:1,xlsx:1", "--count", "2", "--folders", "1",
            "--seed", "42", "--output-path", destination];
        await RunDeliveryAsync(args);

        var request = Cli.Pipeline.Build(args)!;
        var generators = FileGeneratorFactory.CreateMap(request);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] members = ["folder_001/00000001.docx", "folder_001/00000002.xlsx"];
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            var fileType = Path.GetExtension(member)[1..];
            var workItem = new FileWorkItem
            {
                Index = index + 1,
                FolderNumber = 1,
                FolderName = "folder_001",
                FileName = Path.GetFileName(member),
                FilePathInZip = member,
                FileType = fileType,
            };
            var native = generators[fileType].Generate(workItem, request).Content;
            using var package = new ZipArchive(new MemoryStream(native));
            Assert.NotNull(package.GetEntry(fileType == "docx" ? "word/document.xml" : "xl/workbook.xml"));
            expected.Add(member, Convert.ToHexString(SHA256.HashData(native)));
        }

        VerifyOpaqueDelivery(Assert.Single(Directory.GetFiles(destination, "*.zip")), "office-extracted", members, expected, checkOrder: false);
    }

    private static async Task RunDeliveryAsync(params string[] args)
    {
        var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));
        Assert.True(exitCode == 0, $"Program.Main failed with exit code {exitCode}. Stderr: {stderr}, Stdout: {stdout}");
    }

    private void VerifyOpaqueDelivery(
        string zipPath, string directoryName, string[] members, Dictionary<string, string> expected, bool checkOrder = true)
    {
        using var archive = new IndependentZipFile(zipPath);
        var entries = archive.Cast<ICSharpCode.SharpZipLib.Zip.ZipEntry>().ToArray();
        var names = entries.Select(entry => entry.Name);
        Assert.Equal(checkOrder ? members : members.Order(StringComparer.Ordinal), checkOrder ? names : names.Order(StringComparer.Ordinal));

        var extracted = Path.Combine(this.TempDir, directoryName);
        ZipFile.ExtractToDirectory(zipPath, extracted);
        Assert.Equal(members.Order(StringComparer.Ordinal), Directory.GetFiles(extracted, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(extracted, path).Replace('\\', '/')).Order(StringComparer.Ordinal));

        foreach (var entry in entries)
        {
            using var stream = archive.GetInputStream(entry);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            using var disk = File.OpenRead(Path.Combine(extracted, entry.Name));
            Assert.Equal(expected[entry.Name], hash);
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(disk)));
            this.output.WriteLine($"{Path.GetFileName(zipPath)} [{entry.ZipFileIndex}] {entry.Name} {entry.Size} bytes SHA-256 {hash}");
        }
    }

    [Fact]
    public async Task StandardRecipe_GeneratorDeterminism_ProducesIdenticalBytesAcrossFreshInstances()
    {
        var args = new[]
        {
            "--types", "pdf:1,docx:1,eml:1,tiff:1",
            "--count", "4",
            "--output-path", this.TempDir,
            "--include-load-file",
            "--loadfile-format", "dat",
            "--with-families",
            "--attachment-rate", "100",
            "--with-text",
            "--seed", "42",
        };

        var request1 = Cli.Pipeline.Build(args)!;
        var request2 = Cli.Pipeline.Build(args)!;
        var generators1 = FileGeneratorFactory.CreateMap(request1);

        await Task.Delay(10);
        var generators2 = FileGeneratorFactory.CreateMap(request2);

        for (long i = 1; i <= request1.Output.FileCount; i++)
        {
            var fileType = request1.Output.ResolveFileType(i);
            var folderNumber = Distributions.GetFolderNumber(i, request1.Output.FileCount, request1.Output.Folders, request1.LoadFile.Distribution);
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

            var content1 = generators1[fileType].Generate(workItem, request1);
            var content2 = generators2[fileType].Generate(workItem, request2);

            Assert.Equal(content1.Content, content2.Content);

            var attach1 = content1.GetAttachments();
            var attach2 = content2.GetAttachments();
            Assert.Equal(attach1.Count, attach2.Count);
            for (int a = 0; a < attach1.Count; a++)
            {
                Assert.Equal(attach1[a].filename, attach2[a].filename);
                Assert.Equal(attach1[a].content, attach2[a].content);
            }
        }
    }

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
            Assert.True(expected.Rows.ContainsKey("DOC00000003_A001"), "Expected child row DOC00000003_A001 must exist.");
            Assert.Equal("folder_001/3_attachment.jpg", expected.Rows["DOC00000003_A001"].NativePath);
            Assert.Equal("folder_001/3_attachment.txt", expected.Rows["DOC00000003_A001"].TextPath);
            Assert.Equal("DOC00000003", expected.Rows["DOC00000003_A001"].ParentDocId);

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
            var pdfMember = expected.Rows["DOC00000001"].NativePath;
            Assert.EndsWith(".pdf", pdfMember, StringComparison.OrdinalIgnoreCase);
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
            var textPath = $"{folderPrefix}{i:D8}.txt";
            AddExpectedHash(hashes, textPath, textContent);

            var controlNumber = $"DOC{i:D8}";
            AddExpectedRow(rows, controlNumber, workItem.FilePathInZip, textPath, string.Empty);

            var attachments = generated.GetAttachments();
            for (int attachmentIndex = 0; attachmentIndex < attachments.Count; attachmentIndex++)
            {
                var attachmentNumber = attachmentIndex + 1;
                var attachment = attachments[attachmentIndex];
                // Pin expected layout and child ID independently of FamilyPlan / ZIP writer logic.
                var childControlNumber = $"{controlNumber}_A{attachmentNumber:D3}";
                var attachmentFileName = Path.GetFileName(attachment.filename.Replace('\\', '/'));
                var attachmentStem = Path.GetFileNameWithoutExtension(attachmentFileName);
                var attachmentPath = $"{folderPrefix}{i}_{attachmentFileName}";
                var attachmentTextPath = $"{folderPrefix}{i}_{attachmentStem}.txt";

                AddExpectedHash(hashes, attachmentPath, attachment.content);
                AddExpectedHash(hashes, attachmentTextPath, PlaceholderFiles.ExtractedText);
                AddExpectedRow(
                    rows,
                    childControlNumber,
                    attachmentPath,
                    attachmentTextPath,
                    controlNumber);
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
        string textPath,
        string parentDocId)
    {
        Assert.True(
            expected.TryAdd(
                controlNumber,
                new StandardDeliveryRow(nativePath.Replace('\\', '/'), textPath.Replace('\\', '/'), parentDocId)),
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
