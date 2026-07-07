using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class ParallelFileGeneratorTests
{
    [Fact]
    public async Task GenerateFilesAsync_ShouldCreateCorrectCount()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var result = await generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 10,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 2,
                },
            });

            Assert.Equal(10, result.FilesGenerated);
            Assert.True(File.Exists(result.ZipFilePath));

            // Verify zip contains correct number of files
            using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
            Assert.Equal(10, archive.Entries.Count); // Count should match requested files
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_WithTargetZipSize_PadsFilesToReachTargetSize()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            // Request a 1MB ZIP file
            var targetSize = 1_000_000; // 1MB
            var generator = new ParallelFileGenerator();
            var result = await generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 10,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 2,
                    TargetZipSize = targetSize,
                },
            });

            Assert.Equal(10, result.FilesGenerated);
            Assert.True(File.Exists(result.ZipFilePath));

            // REQ-025: the final archive must fall within +/-10% of the target.
            // Padding is random/incompressible, so the compressed result tracks
            // the target closely; assert both-sided tolerance, not just a floor.
            var actualSize = new FileInfo(result.ZipFilePath).Length;
            var deviation = Math.Abs(actualSize - targetSize) / (double)targetSize;
            Assert.True(
                deviation <= 0.10,
                $"ZIP size {actualSize} bytes deviates {deviation:P1} from target {targetSize} bytes (REQ-025 allows +/-10%)");

            // Verify files were created (padding increases file sizes)
            using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
            Assert.Equal(10, archive.Entries.Count);

            // Verify entries have non-trivial sizes due to padding
            foreach (var entry in archive.Entries)
            {
                Assert.True(
                    entry.CompressedLength > 1000,
                    $"Entry {entry.FullName} has compressed length {entry.CompressedLength}, expected > 1000 bytes due to padding");
            }
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_WithImpossibleTargetSize_ThrowsInvalidOperationException()
    {
        // REQ-026: When estimated minimum compressed size already exceeds target, abort immediately.
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            // Request a very small target size (1KB) with many files — impossible to fit
            var generator = new ParallelFileGenerator();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.GenerateFilesAsync(new FileGenerationRequest
                {
                    Output = new OutputConfig
                    {
                        OutputPath = outputPath,
                        FileCount = 100,
                        FileType = "pdf",
                        Folders = 1,
                        Concurrency = 1,
                        TargetZipSize = 100, // 100 bytes — impossibly small for 100 PDFs,
                    },
                }));

            Assert.Contains("target ZIP size", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    // === C1: Bounded work channel prevents OOM at scale ===
    [Fact]
    public async Task GenerateFilesAsync_LargeFileCount_DoesNotDeadlock()
    {
        // C1 regression: unbounded work channel materialized ALL work items upfront,
        // causing OOM. Bounded channel with backpressure must still complete.
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var task = generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 1000,
                    FileType = "pdf",
                    Folders = 5,
                    Concurrency = 4,
                },
            });

            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60))) == task;
            Assert.True(completed, "GenerateFilesAsync with 1000 files did not complete within 60s (possible deadlock from bounded channel)");

            var result = await task;
            Assert.Equal(1000, result.FilesGenerated);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    // === B3: Padding truncation must not corrupt subsequent files ===
    [Fact]
    public async Task GenerateFilesAsync_MultipleFilesWithPadding_AllFilesGenerated()
    {
        // B3 regression: padding per file must apply independently.
        // Previous code mutated shared paddingPerFile var on cap.
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var result = await generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 25,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 4,
                    TargetZipSize = 2_000_000,
                },
            });

            Assert.Equal(25, result.FilesGenerated);
            Assert.True(File.Exists(result.ZipFilePath));

            using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
            Assert.Equal(25, archive.Entries.Count);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    // === B1: Pipeline must not deadlock on errors ===
    [Fact]
    public async Task GenerateFilesAsync_CompletesWithinTimeout()
    {
        // B1 regression: fire-and-forget Task.Run in CreateWorkChannel could deadlock
        // if an exception prevented writer.Complete(). Verify pipeline always completes.
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var task = generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 5,
                    FileType = "pdf",
                    Folders = 1,
                    Concurrency = 2,
                },
            });

            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))) == task;
            Assert.True(completed, "GenerateFilesAsync did not complete within 30s timeout (possible deadlock)");

            var result = await task;
            Assert.Equal(5, result.FilesGenerated);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_InvalidFolders_PropagatesException()
    {
        // B1 regression: exception inside CreateWorkChannel's Task.Run must propagate
        // via writer.Complete(ex), not hang the pipeline. Folders=0 triggers
        // ArgumentOutOfRangeException in FileDistributionHelper.GetFolderNumber.
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            {
                var task = generator.GenerateFilesAsync(new FileGenerationRequest
                {
                    Output = new OutputConfig
                    {
                        OutputPath = outputPath,
                        FileCount = 10,
                        FileType = "pdf",
                        Folders = 0,
                        Concurrency = 2,
                    },
                });

                var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))) == task;
                Assert.True(completed, "GenerateFilesAsync did not complete within timeout (exception was swallowed, pipeline deadlocked)");

                var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => task);
                Assert.Equal("totalFolders", ex.ParamName);
            }
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_FileCountZero_ThrowsArgumentException()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                generator.GenerateFilesAsync(new FileGenerationRequest
                {
                    Output = new OutputConfig
                    {
                        OutputPath = outputPath,
                        FileCount = 0,
                        FileType = "pdf",
                        Folders = 1,
                    },
                }));

            Assert.Contains("File count must be positive", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_FileCountOne_GeneratesSingleFile()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var result = await generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 1,
                    FileType = "pdf",
                    Folders = 1,
                },
            });

            Assert.Equal(1, result.FilesGenerated);

            using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
            Assert.Single(archive.Entries);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_UnknownFileType_ThrowsInvalidOperationException()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.GenerateFilesAsync(new FileGenerationRequest
                {
                    Output = new OutputConfig
                    {
                        OutputPath = outputPath,
                        FileCount = 5,
                        FileType = "unknown",
                        Folders = 1,
                    },
                }));

            Assert.Contains("Unknown file type", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_ConcurrencyZero_UsesDefaultConcurrency()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var result = await generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 10,
                    FileType = "pdf",
                    Folders = 1,
                    Concurrency = 0,
                },
            });

            Assert.Equal(10, result.FilesGenerated);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_ConcurrencyExceedsFileCount_WorksCorrectly()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var result = await generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 3,
                    FileType = "pdf",
                    Folders = 1,
                    Concurrency = 10,
                },
            });

            Assert.Equal(3, result.FilesGenerated);

            using var archive = System.IO.Compression.ZipFile.OpenRead(result.ZipFilePath);
            Assert.Equal(3, archive.Entries.Count);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_NullOutputPath_ThrowsArgumentNullException()
    {
        var generator = new ParallelFileGenerator();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = null!,
                    FileCount = 5,
                    FileType = "pdf",
                    Folders = 1,
                },
            }));
    }

    [Theory]
    [InlineData(10_485_760, 1000, 100, false, 102_857)]
    [InlineData(104_857_600, 1000, 1000, false, 102_857)]
    [InlineData(104_857_600, 1000, 500, false, 207_715)]
    public void CalculatePaddingPerFile_AtKnownInputs_ProducesPinnedValue(
        long targetSize, int baseSize, long fileCount, bool withText, long expectedPadding)
    {
        var generator = new ParallelFileGenerator();
        var result = generator.CalculatePaddingPerFile(targetSize, baseSize, fileCount, withText);
        Assert.Equal(expectedPadding, result);
    }

    [Fact(Timeout = 10000)]
    public async Task GenerateFilesAsync_ConsumerFaults_PipelineTerminatesWithException()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var faultingSink = new InMemorySink
            {
                IsFaulted = true,
                FaultException = new IOException("Simulated consumer fault")
            };
            var generator = new ParallelFileGenerator(faultingSink);

            // This should throw (not hang) because the consumer throws an exception
            var generationTask = generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 100,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 4,
                },
            });

            var completed = await Task.WhenAny(generationTask, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(completed == generationTask, "GenerateFilesAsync did not fail within timeout.");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => generationTask);

            Assert.Equal("Simulated consumer fault", ex.Message);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public void GenerateFileData_NonPlaceholderWithMaxPadding_TakesFallbackPath()
    {
        var generator = new ParallelFileGenerator();
        var workItem = new FileWorkItem { Index = 1 };
        var paddingPerFile = PerformanceConstants.MaxPaddingPerFile;
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "dummy",
                FileType = "docx",
                FileCount = 1,
            }
        };
        var fileGenerator = new OfficeFileGenerator("docx");

        var fileData = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);

        // Correct path selection means it should not use MemoryPool rent and therefore MemoryOwner should be null
        Assert.Null(fileData.MemoryOwner);
        Assert.False(fileData.Data.IsEmpty);
        Assert.Equal(fileData.DataLength, fileData.Data.Length);
    }

    [Fact]
    public void GenerateFileData_WithSeedAndTargetZipSize_PooledPath_ProducesDeterministicHash()
    {
        var generator = new ParallelFileGenerator();
        var workItem = new FileWorkItem { Index = 1 };
        var paddingPerFile = 1000; // Small padding -> pooled path
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "dummy",
                FileType = "docx",
                FileCount = 1,
            },
            Metadata = new MetadataConfig
            {
                Seed = 42
            }
        };
        var fileGenerator = new OfficeFileGenerator("docx");

        var fileData1 = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);
        var fileData2 = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);

        try
        {
            Assert.Equal(fileData1.Hash, fileData2.Hash);
        }
        finally
        {
            fileData1.MemoryOwner?.Dispose();
            fileData2.MemoryOwner?.Dispose();
        }
    }

    [Fact]
    public void GenerateFileData_WithSeedAndTargetZipSize_NonPooledPath_ProducesDeterministicHash()
    {
        var generator = new ParallelFileGenerator();
        var workItem = new FileWorkItem { Index = 1 };
        var paddingPerFile = PerformanceConstants.MaxPaddingPerFile; // Max padding -> non-pooled path
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "dummy",
                FileType = "docx",
                FileCount = 1,
            },
            Metadata = new MetadataConfig
            {
                Seed = 42
            }
        };
        var fileGenerator = new OfficeFileGenerator("docx");

        var fileData1 = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);
        var fileData2 = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);

        try
        {
            Assert.Equal(fileData1.Hash, fileData2.Hash);
        }
        finally
        {
            fileData1.MemoryOwner?.Dispose();
            fileData2.MemoryOwner?.Dispose();
        }
    }

    [Fact]
    public void GenerateFileData_WithHashModeActual_ComputesAllConfiguredHashes()
    {
#pragma warning disable S4426 // Weak cryptographic algorithms are tested for correctness
        var generator = new ParallelFileGenerator();
        var workItem = new FileWorkItem { Index = 1 };
        const int paddingPerFile = 0;
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "dummy",
                FileType = "docx",
                FileCount = 1,
            },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5, Config.HashAlgorithm.SHA1, Config.HashAlgorithm.SHA256 },
            },
        };
        var fileGenerator = new OfficeFileGenerator("docx");

        var fileData = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);

        try
        {
            Assert.NotNull(fileData.Hashes);
            Assert.Equal(3, fileData.Hashes!.Count);
            Assert.True(fileData.Hashes.ContainsKey(Config.HashAlgorithm.MD5));
            Assert.True(fileData.Hashes.ContainsKey(Config.HashAlgorithm.SHA1));
            Assert.True(fileData.Hashes.ContainsKey(Config.HashAlgorithm.SHA256));

            // Verify each hash is a valid lowercase hex string of correct length
            Assert.Equal(32, fileData.Hashes[Config.HashAlgorithm.MD5].Length);
            Assert.Equal(40, fileData.Hashes[Config.HashAlgorithm.SHA1].Length);
            Assert.Equal(64, fileData.Hashes[Config.HashAlgorithm.SHA256].Length);

            // Verify MD5 is also set on the Hash property (backward compat)
            Assert.Equal(fileData.Hashes[Config.HashAlgorithm.MD5], fileData.Hash);

            // Verify hashes are actual hashes of the content bytes
            var contentSpan = fileData.Data.Span;
            var expectedMd5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(contentSpan)).ToLowerInvariant();
#pragma warning disable S4426 // Weak cryptographic algorithms are tested for correctness
#pragma warning disable CA5350 // Weak cryptographic algorithm is used for e-discovery compat
            var expectedSha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(contentSpan)).ToLowerInvariant();
#pragma warning restore CA5350
#pragma warning restore S4426
            var expectedSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contentSpan)).ToLowerInvariant();
            Assert.Equal(expectedMd5, fileData.Hashes[Config.HashAlgorithm.MD5]);
            Assert.Equal(expectedSha1, fileData.Hashes[Config.HashAlgorithm.SHA1]);
            Assert.Equal(expectedSha256, fileData.Hashes[Config.HashAlgorithm.SHA256]);
        }
        finally
        {
            fileData.MemoryOwner?.Dispose();
        }
#pragma warning restore S4426
    }

    [Fact]
    public void GenerateFileData_WithHashModeNone_HashesIsNull()
    {
        var generator = new ParallelFileGenerator();
        var workItem = new FileWorkItem { Index = 1 };
        const int paddingPerFile = 0;
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "dummy",
                FileType = "docx",
                FileCount = 1,
            },
            Hash = new HashConfig
            {
                Mode = HashMode.None,
                Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5 },
            },
        };
        var fileGenerator = new OfficeFileGenerator("docx");

        var fileData = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);

        try
        {
            Assert.Null(fileData.Hashes);
            Assert.NotEmpty(fileData.Hash); // MD5 is still computed internally
        }
        finally
        {
            fileData.MemoryOwner?.Dispose();
        }
    }

    [Fact]
    public void GenerateFileData_WithHashModeSimulated_GeneratesDeterministicSimulatedHashes()
    {
        var generator = new ParallelFileGenerator();
        var workItem = new FileWorkItem { Index = 1 };
        const int paddingPerFile = 0;
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "dummy",
                FileType = "docx",
                FileCount = 1,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Hash = new HashConfig
            {
                Mode = HashMode.Simulated,
                Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5, Config.HashAlgorithm.SHA256 },
            },
        };
        var fileGenerator = new OfficeFileGenerator("docx");

        var fileData1 = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);
        var fileData2 = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);

        try
        {
            Assert.NotNull(fileData1.Hashes);
            Assert.NotNull(fileData2.Hashes);
            Assert.Equal(fileData1.Hashes![Config.HashAlgorithm.MD5], fileData2.Hashes![Config.HashAlgorithm.MD5]);
            Assert.Equal(fileData1.Hashes[Config.HashAlgorithm.SHA256], fileData2.Hashes[Config.HashAlgorithm.SHA256]);
            Assert.Equal(32, fileData1.Hashes[Config.HashAlgorithm.MD5].Length);
            Assert.Equal(64, fileData1.Hashes[Config.HashAlgorithm.SHA256].Length);

            // Simulated hashes should NOT match actual content hashes
            var contentSpan = fileData1.Data.Span;
            var actualMd5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(contentSpan)).ToLowerInvariant();
            Assert.NotEqual(actualMd5, fileData1.Hashes[Config.HashAlgorithm.MD5]);
        }
        finally
        {
            fileData1.MemoryOwner?.Dispose();
            fileData2.MemoryOwner?.Dispose();
        }
    }

    [Fact]
    public async Task GenerateFilesAsync_WithChaosModeAndNoLoadfileOnly_ThrowsInvalidOperationException()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var generator = new ParallelFileGenerator();
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 1,
                    FileType = "pdf",
                },
                Chaos = new ChaosConfig
                {
                    ChaosMode = true,
                },
                LoadfileOnly = false,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateFilesAsync(request));
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact]
    public void GenerateFileData_TotalSizeEqualsMaxPoolSize_UsesMemoryOwner()
    {
        var generator = new ParallelFileGenerator();
        var workItem = new FileWorkItem { Index = 1 };
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = "dummy",
                FileType = "docx",
                FileCount = 1,
            }
        };
        var fileGenerator = new OfficeFileGenerator("docx");
        var content = fileGenerator.Generate(workItem, request).Content;

        var paddingPerFile = PerformanceConstants.MaxPoolSize - content.Length;

        var fileData = generator.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);

        Assert.NotNull(fileData.MemoryOwner);
        Assert.Equal(PerformanceConstants.MaxPoolSize, fileData.DataLength);

        fileData.MemoryOwner?.Dispose();
    }
}
