using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;

namespace Zipper.Tests;

public sealed class ActualHashIntegrationTests : TempDirectoryTestBase
{
    private sealed class CapturingDiskMaterializer : IFileMaterializer
    {
        private readonly ProductionFileMaterializer _inner = new();

        public List<FileData> CapturedFileData { get; } = new();

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
            => _inner.CreateDirectoryAsync(path, cancellationToken);

        public Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
            => _inner.WriteBytesAsync(path, content, cancellationToken);

        public Task WriteTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default)
            => _inner.WriteTextAsync(path, text, encoding, cancellationToken);

        public Stream OpenWriteStream(string path)
            => _inner.OpenWriteStream(path);

        public void AddFileData(FileData data)
        {
            _inner.AddFileData(data);
            this.CapturedFileData.Add(data);
        }

        public Task WriteChildAttachmentAsync(string childNativePath, (string filename, byte[] content) attach, CancellationToken cancellationToken = default)
            => _inner.WriteChildAttachmentAsync(childNativePath, attach, cancellationToken);

        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
            => _inner.DeleteDirectoryAsync(path, cancellationToken);

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
            => _inner.DeleteFileAsync(path, cancellationToken);

        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
            => _inner.DirectoryExistsAsync(path, cancellationToken);

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
            => _inner.FileExistsAsync(path, cancellationToken);

        public Task CreateZipAsync(string sourceDir, string zipPath, CancellationToken cancellationToken = default)
            => _inner.CreateZipAsync(sourceDir, zipPath, cancellationToken);

        public Task CreateZipAsync(string sourceDir, string zipPath, Config.ZipCompressionMethod method, CancellationToken cancellationToken = default)
            => _inner.CreateZipAsync(sourceDir, zipPath, method, cancellationToken);
    }

    [Theory]
    [InlineData(LoadFileFormat.Dat, Config.HashAlgorithm.MD5)]
    [InlineData(LoadFileFormat.Dat, Config.HashAlgorithm.SHA1)]
    [InlineData(LoadFileFormat.Dat, Config.HashAlgorithm.SHA256)]
    [InlineData(LoadFileFormat.Csv, Config.HashAlgorithm.MD5)]
    [InlineData(LoadFileFormat.Csv, Config.HashAlgorithm.SHA1)]
    [InlineData(LoadFileFormat.Csv, Config.HashAlgorithm.SHA256)]
    [InlineData(LoadFileFormat.Concordance, Config.HashAlgorithm.MD5)]
    [InlineData(LoadFileFormat.Concordance, Config.HashAlgorithm.SHA1)]
    [InlineData(LoadFileFormat.Concordance, Config.HashAlgorithm.SHA256)]
    [InlineData(LoadFileFormat.EdrmXml, Config.HashAlgorithm.MD5)]
    [InlineData(LoadFileFormat.EdrmXml, Config.HashAlgorithm.SHA1)]
    [InlineData(LoadFileFormat.EdrmXml, Config.HashAlgorithm.SHA256)]
    public async Task StandardMode_AllAlgorithms_LoadFilesMatchArchiveBytesEndToEnd(
        LoadFileFormat format,
        Config.HashAlgorithm algorithm)
    {
        const int expectedCount = 3;
        var request = CreateBaseStandardRequest(fileCount: expectedCount, fileType: "pdf", seed: 42);
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { format } };
        request.Hash = new HashConfig
        {
            Mode = HashMode.Actual,
            Algorithms = new HashSet<Config.HashAlgorithm> { algorithm },
        };

        if (format == LoadFileFormat.Dat)
        {
            request.Metadata = request.Metadata with
            {
                ColumnProfile = new ColumnProfile
                {
                    Name = $"dat-{algorithm}",
                    Columns = new List<ColumnDefinition>
                    {
                        new() { Name = "Control Number", Type = "text" },
                        new() { Name = "File Path", Type = "text" },
                        new() { Name = $"{algorithm.ToString().ToUpperInvariant()}HASH", Type = "text" },
                    },
                },
            };
        }

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        Assert.True(File.Exists(result.ZipFilePath), "Archive ZIP file must exist.");
        using var archive = ZipFile.OpenRead(result.ZipFilePath);

        var records = ParseEmittedLoadFileRecords(result.LoadFilePath, format, algorithm);
        Assert.Equal(expectedCount, records.Count);

        foreach (var record in records)
        {
            AssertExpectedHashFormat(record.EmittedHash, algorithm);
            var entry = archive.GetEntry(record.FilePath);
            Assert.NotNull(entry);

            var entryBytes = ReadArchiveEntryBytes(entry);
            var expectedHash = ComputeIndependentDigest(entryBytes, algorithm);
            Assert.Equal(expectedHash, record.EmittedHash);
        }
    }

    [Theory]
    [InlineData(Config.HashAlgorithm.MD5)]
    [InlineData(Config.HashAlgorithm.SHA1)]
    [InlineData(Config.HashAlgorithm.SHA256)]
    public async Task StandardMode_SeededEmailWithFamilies_EdrmXmlChildHashRefersToChildBytes(
        Config.HashAlgorithm algorithm)
    {
        const int parentCount = 4;
        var request = CreateBaseStandardRequest(fileCount: parentCount, fileType: "eml", seed: 42);
        request.Metadata = request.Metadata with { WithFamilies = true, Seed = 42 };
        request.LoadFile = request.LoadFile with
        {
            AttachmentRate = 100,
            Formats = new List<LoadFileFormat> { LoadFileFormat.EdrmXml },
        };
        request.Hash = new HashConfig
        {
            Mode = HashMode.Actual,
            Algorithms = new HashSet<Config.HashAlgorithm> { algorithm },
        };

        var emlGenerator = new EmlFileGenerator();
        var processedFiles = new List<FileData>();
        var parentBytesList = new List<byte[]>();
        var childBytesList = new List<byte[]>();

        for (int i = 1; i <= parentCount; i++)
        {
            var workItem = new FileWorkItem
            {
                Index = i,
                FolderNumber = 1,
                FolderName = "0001",
                FileName = $"0001_{i:D8}.eml",
                FilePathInZip = $"0001/0001_{i:D8}.eml",
                FileType = "eml",
            };

            var generated = emlGenerator.Generate(workItem, request);
            Assert.True(generated.Attachment.HasValue, "Attachment must be generated with 100% attachment rate.");

            var hashes = HashComputer.ComputeHashes(generated.Content, request.Hash, workItem, request);
            var fileData = new FileData
            {
                WorkItem = workItem,
                Data = generated.Content,
                DataLength = generated.Content.Length,
                Attachment = generated.Attachment,
                Email = generated.Email,
                Hashes = hashes,
                Hash = hashes is not null && hashes.TryGetValue(Config.HashAlgorithm.MD5, out var md5) ? md5 : string.Empty,
            };

            processedFiles.Add(fileData);
            parentBytesList.Add(generated.Content);
            childBytesList.Add(generated.Attachment.Value.content);
        }

        var xmlPath = Path.Combine(this.TempDir, $"edrm_families_{algorithm}_{Guid.NewGuid():N}.xml");
        await using (var xmlStream = new FileStream(xmlPath, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, useAsync: true))
        {
            await new XmlLoadFileWriter().WriteAsync(xmlStream, request, processedFiles);
        }

        var familyRecords = ParseEdrmXmlFamilyRecords(xmlPath, algorithm);
        Assert.Equal(parentCount, familyRecords.Count);

        int totalCheckedRecords = 0;
        for (int i = 0; i < parentCount; i++)
        {
            var family = familyRecords[i];
            var parentBytes = parentBytesList[i];
            var childBytes = childBytesList[i];

            var expectedParentHash = ComputeIndependentDigest(parentBytes, algorithm);
            var expectedChildHash = ComputeIndependentDigest(childBytes, algorithm);

            AssertExpectedHashFormat(family.ParentHash, algorithm);
            Assert.Equal(expectedParentHash, family.ParentHash);
            totalCheckedRecords++;

            Assert.NotNull(family.ChildFilePath);
            Assert.NotNull(family.ChildHash);

            AssertExpectedHashFormat(family.ChildHash, algorithm);
            Assert.Equal(expectedChildHash, family.ChildHash);
            totalCheckedRecords++;

            // Crucial: Child hash must refer to child bytes, not parent Email.
            Assert.NotEqual(family.ParentHash, family.ChildHash);
            Assert.NotEqual(parentBytes.Length, childBytes.Length);
        }

        Assert.Equal(parentCount * 2, totalCheckedRecords);
    }

    [Theory]
    [InlineData(Config.HashAlgorithm.MD5)]
    [InlineData(Config.HashAlgorithm.SHA1)]
    [InlineData(Config.HashAlgorithm.SHA256)]
    public async Task StandardMode_StandardPadding_HashBindsFinalPaddedArchiveBytes(
        Config.HashAlgorithm algorithm)
    {
        const int fileCount = 3;
        var request = CreateBaseStandardRequest(fileCount: fileCount, fileType: "docx", seed: 42);
        request.Output = request.Output with
        {
            Concurrency = 1,
            TargetZipSize = 300_000,
        };
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Csv } };
        request.Hash = new HashConfig
        {
            Mode = HashMode.Actual,
            Algorithms = new HashSet<Config.HashAlgorithm> { algorithm },
        };

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        Assert.True(File.Exists(result.ZipFilePath), "Archive ZIP file must exist.");
        using var archive = ZipFile.OpenRead(result.ZipFilePath);

        var records = ParseEmittedLoadFileRecords(result.LoadFilePath, LoadFileFormat.Csv, algorithm);
        Assert.Equal(fileCount, records.Count);

        var baseContentLength = PlaceholderFiles.GetContent("docx").Length;

        foreach (var record in records)
        {
            var entry = archive.GetEntry(record.FilePath);
            Assert.NotNull(entry);
            Assert.True(entry.Length > baseContentLength, "Emitted entry should contain padded bytes beyond base docx content.");

            var entryBytes = ReadArchiveEntryBytes(entry);
            var expectedPaddedHash = ComputeIndependentDigest(entryBytes, algorithm);
            Assert.Equal(expectedPaddedHash, record.EmittedHash);

            // Verify hash covers full padded bytes, not just unpadded prefix
            var unpaddedPrefix = entryBytes.AsSpan(0, baseContentLength);
            var unpaddedHash = ComputeIndependentDigest(unpaddedPrefix, algorithm);
            Assert.NotEqual(unpaddedHash, record.EmittedHash);
        }
    }

    [Theory]
    [InlineData(Config.HashAlgorithm.MD5)]
    [InlineData(Config.HashAlgorithm.SHA1)]
    [InlineData(Config.HashAlgorithm.SHA256)]
    public async Task StandardMode_SourceDriven_HashesMatchSourceArchiveEntries(
        Config.HashAlgorithm algorithm)
    {
        var rows = new List<Zipper.SourceInput.SourceRecord>
        {
            new() { RelativePath = "contracts/agreement.pdf", FileType = "pdf", ControlNumber = "SRC-001" },
            new() { RelativePath = "notes/memo.eml", FileType = "eml", ControlNumber = "SRC-002" },
            new() { RelativePath = "evidence/scan.tiff", FileType = "tiff", ControlNumber = "SRC-003" },
        };

        var request = CreateBaseStandardRequest(fileCount: rows.Count, fileType: "pdf", seed: 42);
        request.SourceRecords = rows;
        request.Output = request.Output with
        {
            SourceFileTypes = rows.Select(r => r.FileType).Distinct(StringComparer.Ordinal).ToList(),
        };
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Csv } };
        request.Hash = new HashConfig
        {
            Mode = HashMode.Actual,
            Algorithms = new HashSet<Config.HashAlgorithm> { algorithm },
        };

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        Assert.True(File.Exists(result.ZipFilePath), "Archive ZIP file must exist.");
        using var archive = ZipFile.OpenRead(result.ZipFilePath);

        var records = ParseEmittedLoadFileRecords(result.LoadFilePath, LoadFileFormat.Csv, algorithm);
        Assert.Equal(rows.Count, records.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            var expectedPath = rows[i].RelativePath;
            var record = records[i];
            Assert.Equal(expectedPath, record.FilePath);

            var entry = archive.GetEntry(expectedPath);
            Assert.NotNull(entry);
            var entryBytes = ReadArchiveEntryBytes(entry);
            var expectedHash = ComputeIndependentDigest(entryBytes, algorithm);
            Assert.Equal(expectedHash, record.EmittedHash);
        }
    }

    [Theory]
    [InlineData(Config.HashAlgorithm.MD5)]
    [InlineData(Config.HashAlgorithm.SHA1)]
    [InlineData(Config.HashAlgorithm.SHA256)]
    public async Task ProductionSet_AllAlgorithms_MaterializedHashesMatchFinalOnDiskBytes(
        Config.HashAlgorithm algorithm)
    {
        const int expectedCount = 3;
        var prodDir = Path.Combine(this.TempDir, $"ProdSet_{algorithm}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(prodDir);

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = prodDir,
                FileCount = expectedCount,
                FileType = "pdf",
                Concurrency = 1,
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 2,
                ProductionZip = true,
                ProductionId = $"PROD_{algorithm}",
            },
            Bates = new BatesNumberConfig
            {
                Prefix = "PROD",
                Start = 1,
                Digits = 6,
                Increment = 1,
            },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<Config.HashAlgorithm> { algorithm },
            },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
        };

        var materializer = new CapturingDiskMaterializer();
        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.NotNull(result);
        Assert.True(Directory.Exists(result.ProductionPath), "Production directory must exist.");
        Assert.Equal(expectedCount, materializer.CapturedFileData.Count);

        foreach (var fileData in materializer.CapturedFileData)
        {
            var relativeNativePath = fileData.WorkItem.FilePathInZip;
            Assert.False(string.IsNullOrEmpty(relativeNativePath));

            var onDiskPath = Path.Combine(result.ProductionPath, relativeNativePath);
            Assert.True(File.Exists(onDiskPath), $"On-disk Native File must exist at {onDiskPath}");

            var onDiskBytes = await File.ReadAllBytesAsync(onDiskPath);
            var expectedHash = ComputeIndependentDigest(onDiskBytes, algorithm);

            Assert.NotNull(fileData.Hashes);
            Assert.True(fileData.Hashes.TryGetValue(algorithm, out var actualDigest));
            AssertExpectedHashFormat(actualDigest, algorithm);
            Assert.Equal(expectedHash, actualDigest);

            if (algorithm == Config.HashAlgorithm.MD5)
            {
                Assert.Equal(expectedHash, fileData.Hash);
            }
        }

        // Also verify the bundled production ZIP entries
        Assert.NotNull(result.ZipFilePath);
        Assert.True(File.Exists(result.ZipFilePath), "Production ZIP archive must exist.");
        using var archive = ZipFile.OpenRead(result.ZipFilePath);

        foreach (var fileData in materializer.CapturedFileData)
        {
            var normalizedZipPath = fileData.WorkItem.FilePathInZip.Replace('\\', '/');
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith(normalizedZipPath, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(entry);

            var entryBytes = ReadArchiveEntryBytes(entry);
            var expectedHash = ComputeIndependentDigest(entryBytes, algorithm);
            Assert.Equal(expectedHash, fileData.Hashes![algorithm]);
        }
    }

    [Fact]
    public async Task ProductionSet_SeededEmailWithFamilies_OnDiskChildBytesDistinctAndHashed()
    {
        var prodDir = Path.Combine(this.TempDir, $"ProdFam_{Guid.NewGuid():N}");
        Directory.CreateDirectory(prodDir);

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = prodDir,
                FileCount = 2,
                FileType = "eml",
                Concurrency = 1,
            },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 5,
                ProductionId = "EMLFAM",
            },
            Bates = new BatesNumberConfig
            {
                Prefix = "EMLFAM",
                Start = 1,
                Digits = 6,
                Increment = 1,
            },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<Config.HashAlgorithm>
                {
                    Config.HashAlgorithm.MD5,
                    Config.HashAlgorithm.SHA1,
                    Config.HashAlgorithm.SHA256,
                },
            },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8", AttachmentRate = 100 },
        };

        var materializer = new CapturingDiskMaterializer();
        var result = await ProductionSetOrchestrator.GenerateAsync(request, materializer, new HashComputer());

        Assert.NotNull(result);
        Assert.Equal(2, materializer.CapturedFileData.Count);

        foreach (var fileData in materializer.CapturedFileData)
        {
            var parentDiskPath = Path.Combine(result.ProductionPath, fileData.WorkItem.FilePathInZip);
            Assert.True(File.Exists(parentDiskPath), $"Parent EML must exist on disk: {parentDiskPath}");
            var parentBytes = await File.ReadAllBytesAsync(parentDiskPath);

            var expectedParentMd5 = ComputeIndependentDigest(parentBytes, Config.HashAlgorithm.MD5);
            var expectedParentSha1 = ComputeIndependentDigest(parentBytes, Config.HashAlgorithm.SHA1);
            var expectedParentSha256 = ComputeIndependentDigest(parentBytes, Config.HashAlgorithm.SHA256);

            Assert.Equal(expectedParentMd5, fileData.Hashes![Config.HashAlgorithm.MD5]);
            Assert.Equal(expectedParentSha1, fileData.Hashes[Config.HashAlgorithm.SHA1]);
            Assert.Equal(expectedParentSha256, fileData.Hashes[Config.HashAlgorithm.SHA256]);

            // Locate child attachment file in NATIVES
            var parentStem = Path.GetFileNameWithoutExtension(fileData.WorkItem.FilePathInZip);
            var volDir = Path.GetDirectoryName(parentDiskPath)!;
            var childFiles = Directory.GetFiles(volDir, $"{parentStem}_A001.*");
            var childDiskPath = Assert.Single(childFiles);

            var childBytes = await File.ReadAllBytesAsync(childDiskPath);
            Assert.NotEmpty(childBytes);
            Assert.NotEqual(parentBytes.Length, childBytes.Length);

            var expectedChildMd5 = ComputeIndependentDigest(childBytes, Config.HashAlgorithm.MD5);
            var expectedChildSha256 = ComputeIndependentDigest(childBytes, Config.HashAlgorithm.SHA256);

            // Child hash must refer to child bytes, not parent Email
            Assert.NotEqual(expectedParentMd5, expectedChildMd5);
            Assert.NotEqual(expectedParentSha256, expectedChildSha256);

            // Verify child file is explicitly mapped in emitted Production Set loadfile.dat
            var datPath = Path.Combine(result.ProductionPath, "DATA", "loadfile.dat");
            Assert.True(File.Exists(datPath), "DATA/loadfile.dat must exist on disk.");
            var datContent = await File.ReadAllTextAsync(datPath);
            var childRelNative = Path.GetRelativePath(result.ProductionPath, childDiskPath).Replace('/', '\\');
            Assert.Contains(childRelNative, datContent, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task StandardMode_MultiAlgorithm_AllConfiguredDigestsEqualArchiveBytes()
    {
        const int fileCount = 3;
        var request = CreateBaseStandardRequest(fileCount: fileCount, fileType: "pdf", seed: 42);
        request.LoadFile = request.LoadFile with
        {
            Formats = new List<LoadFileFormat> { LoadFileFormat.Csv },
        };
        request.Hash = new HashConfig
        {
            Mode = HashMode.Actual,
            Algorithms = new HashSet<Config.HashAlgorithm>
            {
                Config.HashAlgorithm.MD5,
                Config.HashAlgorithm.SHA1,
                Config.HashAlgorithm.SHA256,
            },
        };

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);

        Assert.True(File.Exists(result.ZipFilePath), "Archive ZIP file must exist.");
        using var archive = ZipFile.OpenRead(result.ZipFilePath);

        var csvRecordsMd5 = ParseEmittedLoadFileRecords(result.LoadFilePath, LoadFileFormat.Csv, Config.HashAlgorithm.MD5);
        var csvRecordsSha1 = ParseEmittedLoadFileRecords(result.LoadFilePath, LoadFileFormat.Csv, Config.HashAlgorithm.SHA1);
        var csvRecordsSha256 = ParseEmittedLoadFileRecords(result.LoadFilePath, LoadFileFormat.Csv, Config.HashAlgorithm.SHA256);

        Assert.Equal(fileCount, csvRecordsMd5.Count);
        Assert.Equal(fileCount, csvRecordsSha1.Count);
        Assert.Equal(fileCount, csvRecordsSha256.Count);

        for (int i = 0; i < fileCount; i++)
        {
            var entry = archive.GetEntry(csvRecordsMd5[i].FilePath);
            Assert.NotNull(entry);
            var entryBytes = ReadArchiveEntryBytes(entry);

            var expectedMd5 = ComputeIndependentDigest(entryBytes, Config.HashAlgorithm.MD5);
            var expectedSha1 = ComputeIndependentDigest(entryBytes, Config.HashAlgorithm.SHA1);
            var expectedSha256 = ComputeIndependentDigest(entryBytes, Config.HashAlgorithm.SHA256);

            Assert.Equal(expectedMd5, csvRecordsMd5[i].EmittedHash);
            Assert.Equal(expectedSha1, csvRecordsSha1[i].EmittedHash);
            Assert.Equal(expectedSha256, csvRecordsSha256[i].EmittedHash);
        }
    }

    [Fact]
    public async Task TamperedDigest_FailsVerificationAgainstArchiveEntryBytes()
    {
        var request = CreateBaseStandardRequest(fileCount: 2, fileType: "eml", seed: 42);
        request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Csv } };
        request.Hash = new HashConfig
        {
            Mode = HashMode.Actual,
            Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.SHA256 },
        };

        var result = await new ParallelFileGenerator().GenerateFilesAsync(request);
        using var archive = ZipFile.OpenRead(result.ZipFilePath);

        var records = ParseEmittedLoadFileRecords(result.LoadFilePath, LoadFileFormat.Csv, Config.HashAlgorithm.SHA256);
        Assert.Equal(2, records.Count);

        // Verification succeeds when un-tampered
        var entry0 = archive.GetEntry(records[0].FilePath)!;
        var bytes0 = ReadArchiveEntryBytes(entry0);
        Assert.Equal(ComputeIndependentDigest(bytes0, Config.HashAlgorithm.SHA256), records[0].EmittedHash);

        // Cross-row mismatch: row 0 hash does NOT equal row 1 archive bytes
        var entry1 = archive.GetEntry(records[1].FilePath)!;
        var bytes1 = ReadArchiveEntryBytes(entry1);
        Assert.NotEqual(ComputeIndependentDigest(bytes1, Config.HashAlgorithm.SHA256), records[0].EmittedHash);

        // Tampered byte in entry does NOT match emitted hash
        var tamperedBytes = (byte[])bytes0.Clone();
        tamperedBytes[0] ^= 0xFF;
        var tamperedDigest = ComputeIndependentDigest(tamperedBytes, Config.HashAlgorithm.SHA256);
        Assert.NotEqual(tamperedDigest, records[0].EmittedHash);
    }

    private FileGenerationRequest CreateBaseStandardRequest(int fileCount, string fileType, int seed)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.TempDir,
                FileCount = fileCount,
                FileType = fileType,
                Folders = 1,
                Concurrency = 2,
            },
            Metadata = new MetadataConfig { Seed = seed },
            LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
        };
    }

    private static byte[] ReadArchiveEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string ComputeIndependentDigest(ReadOnlySpan<byte> bytes, Config.HashAlgorithm algorithm)
    {
        return algorithm switch
        {
#pragma warning disable CA5351 // MD5 tested for e-discovery compatibility
            Config.HashAlgorithm.MD5 => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),
#pragma warning restore CA5351
#pragma warning disable CA5350 // SHA-1 tested for e-discovery compatibility
            Config.HashAlgorithm.SHA1 => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(),
#pragma warning restore CA5350
            Config.HashAlgorithm.SHA256 => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), $"Unsupported algorithm: {algorithm}"),
        };
    }

    private static void AssertExpectedHashFormat(string hash, Config.HashAlgorithm algorithm)
    {
        Assert.False(string.IsNullOrWhiteSpace(hash));
        int expectedLen = algorithm switch
        {
            Config.HashAlgorithm.MD5 => 32,
            Config.HashAlgorithm.SHA1 => 40,
            Config.HashAlgorithm.SHA256 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
        Assert.Equal(expectedLen, hash.Length);
        Assert.True(hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    private static List<(string FilePath, string EmittedHash)> ParseEmittedLoadFileRecords(
        string loadFilePath,
        LoadFileFormat format,
        Config.HashAlgorithm algorithm)
    {
        return format switch
        {
            LoadFileFormat.Csv => ParseCsvRecords(loadFilePath, algorithm),
            LoadFileFormat.Concordance => ParseDatOrConcordanceRecords(loadFilePath, algorithm),
            LoadFileFormat.Dat => ParseDatOrConcordanceRecords(loadFilePath, algorithm),
            LoadFileFormat.EdrmXml => ParseEdrmXmlRecords(loadFilePath, algorithm),
            _ => throw new NotSupportedException($"Format {format} parsing is not implemented in this helper."),
        };
    }

    private static List<(string FilePath, string EmittedHash)> ParseCsvRecords(
        string csvPath,
        Config.HashAlgorithm algorithm)
    {
        var lines = File.ReadAllLines(csvPath).Where(l => l.Length > 0).ToList();
        Assert.NotEmpty(lines);

        var headerCols = lines[0].Split(',').Select(h => h.Trim('"', ' ')).ToArray();
        var pathIdx = Array.FindIndex(headerCols, h => string.Equals(h, "FILE PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "File Path", StringComparison.OrdinalIgnoreCase));
        var hashColName = algorithm switch
        {
            Config.HashAlgorithm.MD5 => "MD5HASH",
            Config.HashAlgorithm.SHA1 => "SHA1HASH",
            Config.HashAlgorithm.SHA256 => "SHA256HASH",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
        var hashIdx = Array.FindIndex(headerCols, h => string.Equals(h, hashColName, StringComparison.OrdinalIgnoreCase));

        Assert.True(pathIdx >= 0, $"CSV header missing path column: {lines[0]}");
        Assert.True(hashIdx >= 0, $"CSV header missing {hashColName} column: {lines[0]}");

        var results = new List<(string FilePath, string EmittedHash)>(lines.Count - 1);
        for (int i = 1; i < lines.Count; i++)
        {
            var cols = lines[i].Split(',').Select(c => c.Trim('"', ' ')).ToArray();
            results.Add((cols[pathIdx], cols[hashIdx]));
        }

        return results;
    }

    private static List<(string FilePath, string EmittedHash)> ParseDatOrConcordanceRecords(
        string datPath,
        Config.HashAlgorithm algorithm)
    {
        var lines = File.ReadAllLines(datPath).Where(l => l.Length > 0).ToList();
        Assert.NotEmpty(lines);

        var headerCols = lines[0].Split('\u0014').Select(h => h.Trim('\u00fe', '\r', '\n')).ToArray();
        var pathIdx = Array.FindIndex(headerCols, h => string.Equals(h, "PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "File Path", StringComparison.OrdinalIgnoreCase));
        var hashColName = algorithm switch
        {
            Config.HashAlgorithm.MD5 => "MD5HASH",
            Config.HashAlgorithm.SHA1 => "SHA1HASH",
            Config.HashAlgorithm.SHA256 => "SHA256HASH",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
        var hashIdx = Array.FindIndex(headerCols, h => string.Equals(h, hashColName, StringComparison.OrdinalIgnoreCase));

        Assert.True(pathIdx >= 0, $"DAT/Concordance header missing path column: {lines[0]}");
        Assert.True(hashIdx >= 0, $"DAT/Concordance header missing {hashColName} column: {lines[0]}");

        var results = new List<(string FilePath, string EmittedHash)>(lines.Count - 1);
        for (int i = 1; i < lines.Count; i++)
        {
            var cols = lines[i].Split('\u0014').Select(c => c.Trim('\u00fe', '\r', '\n')).ToArray();
            results.Add((cols[pathIdx], cols[hashIdx]));
        }

        return results;
    }

    private static List<(string FilePath, string EmittedHash)> ParseEdrmXmlRecords(
        string xmlPath,
        Config.HashAlgorithm algorithm)
    {
        var doc = XDocument.Load(xmlPath);
        var batch = doc.Root?.Element("Batch");
        Assert.NotNull(batch);

        var attrName = algorithm switch
        {
            Config.HashAlgorithm.MD5 => "Hash",
            Config.HashAlgorithm.SHA1 => "Sha1Hash",
            Config.HashAlgorithm.SHA256 => "Sha256Hash",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };

        var results = new List<(string FilePath, string EmittedHash)>();
        foreach (var docElem in batch.Elements("Document"))
        {
            var nativeExtFile = docElem.Element("Files")
                ?.Elements("File")
                .FirstOrDefault(f => f.Attribute("FileType")?.Value == "Native")
                ?.Element("ExternalFile");

            if (nativeExtFile is not null)
            {
                var filePath = nativeExtFile.Attribute("FilePath")?.Value;
                var hashVal = nativeExtFile.Attribute(attrName)?.Value;
                Assert.NotNull(filePath);
                Assert.NotNull(hashVal);
                results.Add((filePath, hashVal));
            }
        }

        return results;
    }

    private sealed record FamilyRecord(string ParentFilePath, string ParentHash, string ChildFilePath, string ChildHash);

    private static List<FamilyRecord> ParseEdrmXmlFamilyRecords(
        string xmlPath,
        Config.HashAlgorithm algorithm)
    {
        var doc = XDocument.Load(xmlPath);
        var batch = doc.Root?.Element("Batch");
        Assert.NotNull(batch);

        var attrName = algorithm switch
        {
            Config.HashAlgorithm.MD5 => "Hash",
            Config.HashAlgorithm.SHA1 => "Sha1Hash",
            Config.HashAlgorithm.SHA256 => "Sha256Hash",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };

        var docMap = new Dictionary<string, (string FilePath, string Hash)>(StringComparer.Ordinal);
        foreach (var docElem in batch.Elements("Document"))
        {
            var docId = docElem.Attribute("DocID")?.Value;
            var nativeExtFile = docElem.Element("Files")
                ?.Elements("File")
                .FirstOrDefault(f => f.Attribute("FileType")?.Value == "Native")
                ?.Element("ExternalFile");

            if (docId is not null && nativeExtFile is not null)
            {
                var filePath = nativeExtFile.Attribute("FilePath")?.Value;
                var hashVal = nativeExtFile.Attribute(attrName)?.Value;
                Assert.NotNull(filePath);
                Assert.NotNull(hashVal);
                docMap[docId] = (filePath, hashVal);
            }
        }

        var relationships = batch.Element("Relationships")?.Elements("Relationship");
        Assert.NotNull(relationships);

        var families = new List<FamilyRecord>();
        foreach (var rel in relationships)
        {
            var parentId = rel.Attribute("ParentDocID")?.Value;
            var childId = rel.Attribute("ChildDocID")?.Value;
            Assert.NotNull(parentId);
            Assert.NotNull(childId);

            Assert.True(docMap.TryGetValue(parentId, out var parentInfo));
            Assert.True(docMap.TryGetValue(childId, out var childInfo));

            families.Add(new FamilyRecord(parentInfo.FilePath, parentInfo.Hash, childInfo.FilePath, childInfo.Hash));
        }

        return families;
    }
}
