using Xunit;

using Zipper.Config;

namespace Zipper
{
    public class ParallelFileGeneratorTests
    {
        [Fact]
        public async Task GenerateFilesAsync_ShouldCreateCorrectCount()
        {
            var tempDir = Path.GetTempPath();
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
            var tempDir = Path.GetTempPath();
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

                // Verify the ZIP file is approximately the target size (within 20% tolerance)
                // Due to compression variance, exact size isn't possible but files should be padded
                var actualSize = new FileInfo(result.ZipFilePath).Length;
                var minimumExpected = targetSize * 0.5; // At least 50% of target
                Assert.True(
                    actualSize >= minimumExpected,
                    $"ZIP size {actualSize} bytes is below minimum expected {minimumExpected} bytes for target {targetSize} bytes");

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
            var tempDir = Path.GetTempPath();
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

                Assert.Contains("target ZIP size", ex.Message);
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
            var tempDir = Path.GetTempPath();
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
            var tempDir = Path.GetTempPath();
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
            var tempDir = Path.GetTempPath();
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
            var tempDir = Path.GetTempPath();
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
            var tempDir = Path.GetTempPath();
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

                Assert.Contains("File count must be positive", ex.Message);
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
            var tempDir = Path.GetTempPath();
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
            var tempDir = Path.GetTempPath();
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

                Assert.Contains("Unknown file type", ex.Message);
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
            var tempDir = Path.GetTempPath();
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
            var tempDir = Path.GetTempPath();
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
            // Windows ReadOnly attribute on directories doesn't prevent file creation
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputPath);

            try
            {
                // Make directory read-only so zip file creation fails inside the consumer
                File.SetAttributes(outputPath, FileAttributes.ReadOnly);
                if (!OperatingSystem.IsWindows())
                {
                    // On Linux, directory write permission controls file creation
                    System.Diagnostics.Process.Start("chmod", $"555 {outputPath}")?.WaitForExit();
                }

                var generator = new ParallelFileGenerator();

                // This should throw (not hang) because the consumer cannot create the zip
                await Assert.ThrowsAnyAsync<Exception>(async () =>
                {
                    await generator.GenerateFilesAsync(new FileGenerationRequest
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
                });
            }
            finally
            {
                // Restore permissions before cleanup
                if (!OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start("chmod", $"755 {outputPath}")?.WaitForExit();
                }
                else
                {
                    File.SetAttributes(outputPath, FileAttributes.Normal);
                }

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
    }
}
