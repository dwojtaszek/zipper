using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Zipper.Emails;

namespace Zipper
{
    /// <summary>
    /// Generates files in parallel with controlled concurrency and memory pooling.
    /// </summary>
    public class ParallelFileGenerator
    {
        private readonly PerformanceMonitor performanceMonitor = new PerformanceMonitor();

        public async Task<FileGenerationResult> GenerateFilesAsync(FileGenerationRequest request)
        {
            string? zipFilePath = null;
            string? loadFilePath = null;

            ArgumentNullException.ThrowIfNull(request);

            // Clone to avoid mutating the caller's request object
            request = request.Clone();

            this.performanceMonitor.Start(request.Output.FileCount);

            try
            {
                // Validate inputs
                if (request.Chaos.ChaosMode && !request.LoadfileOnly)
                {
                    throw new InvalidOperationException("Chaos mode requires loadfile-only mode at the generation layer.");
                }

                if (request.Output.FileCount <= 0)
                {
                    throw new ArgumentException("File count must be positive", nameof(request.Output.FileCount));
                }

                if (request.Output.Concurrency <= 0)
                {
                    request.Output = request.Output with { Concurrency = PerformanceConstants.DefaultConcurrency };
                }

                var fileGenerator = FileGeneratorFactory.Create(request.Output.FileType, request)
                    ?? throw new InvalidOperationException($"Unknown file type: {request.Output.FileType}");

                Directory.CreateDirectory(request.Output.OutputPath);

                var baseFileName = $"archive_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                zipFilePath = Path.Combine(request.Output.OutputPath, $"{baseFileName}.zip");
                var loadFileName = $"{baseFileName}.dat";
                loadFilePath = Path.Combine(request.Output.OutputPath, loadFileName);

                var placeholderContent = fileGenerator.IsPlaceholderBased
                    ? PlaceholderFiles.GetContent(request.Output.FileTypeLower)
                    : Array.Empty<byte>();

                long paddingPerFile = 0;
                if (request.Output.TargetZipSize.HasValue)
                {
                    var baseSize = placeholderContent.Length > 0 ? placeholderContent.Length : 1024;
                    paddingPerFile = this.CalculatePaddingPerFile(request.Output.TargetZipSize.Value, baseSize, request.Output.FileCount, request.Output.WithText);
                }

                // Create channels for work distribution
                var workChannelReader = CreateWorkChannel(request.Output.FileCount, request.Output.Folders, request.LoadFile.Distribution, request.Output.FileType, request.Output.Concurrency);
                var resultChannel = Channel.CreateBounded<FileData>(new BoundedChannelOptions(request.Output.Concurrency * 2)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                });

                // Start the consumer task to write the archive concurrently
                var consumerTask = ZipArchiveService.CreateArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, resultChannel.Reader);

                // Generate files in parallel (producers)
                var producerTasks = Enumerable.Range(0, request.Output.Concurrency)
                    .Select(i => this.ProcessFileWorkAsync(workChannelReader, paddingPerFile, resultChannel.Writer, request, fileGenerator))
                    .ToList();

                // Wait for all producers to complete, wrapped in a task that ensures completion
                var allProducersTask = Task.WhenAll(producerTasks);

                // Race producers against consumer — if consumer faults, unblock producers
                Exception? producerException = null;
                try
                {
                    var completed = await Task.WhenAny(allProducersTask, consumerTask).ConfigureAwait(false);
                    if (completed == consumerTask && consumerTask.IsFaulted)
                    {
                        // Consumer died — complete channel with its exception to unblock producers
                        resultChannel.Writer.TryComplete(consumerTask.Exception);
                        try
                        {
                            await allProducersTask.ConfigureAwait(false);
                        }
                        catch
                        {
                            // Producers will get ChannelClosedException — expected
                        }
                    }
                    else
                    {
                        // Producers finished first (normal path)
                        await allProducersTask.ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    producerException = ex;
                }
                finally
                {
                    // Signal production done so consumer can drain and exit
                    resultChannel.Writer.TryComplete(null);
                }

                // Always wait for consumer (releases zip file handles)
                var actualLoadFilePath = await consumerTask.ConfigureAwait(false);

                RethrowIfNotNull(producerException);

                this.performanceMonitor.FinalizeProgress();
                var performanceMetrics = this.performanceMonitor.Stop();

                long actualZipSize = 0;
                ZipSizeVerificationResult? zipSizeVerification = null;

                if (zipFilePath != null && File.Exists(zipFilePath))
                {
                    actualZipSize = new FileInfo(zipFilePath).Length;
                    if (request.Output.TargetZipSize.HasValue)
                    {
                        var (isWithinTolerance, deviation) = ZipSizeVerifier.Verify(request.Output.TargetZipSize.Value, actualZipSize);
                        zipSizeVerification = new ZipSizeVerificationResult(isWithinTolerance, deviation);
                    }
                }

                return new FileGenerationResult
                {
                    ZipFilePath = zipFilePath ?? string.Empty,
                    LoadFilePath = actualLoadFilePath ?? string.Empty,
                    FilesGenerated = request.Output.FileCount,
                    GenerationTime = TimeSpan.FromMilliseconds(performanceMetrics.ElapsedMilliseconds),
                    FilesPerSecond = performanceMetrics.FilesPerSecond,
                    ActualZipSize = actualZipSize,
                    ZipSizeVerification = zipSizeVerification,
                };
            }
            catch
            {
                this.performanceMonitor.Stop();
                try
                {
                    if (zipFilePath != null && File.Exists(zipFilePath)) File.Delete(zipFilePath);
                    if (loadFilePath != null && File.Exists(loadFilePath)) File.Delete(loadFilePath);
                }
#pragma warning disable CA1031
#pragma warning disable RCS1075
                catch (Exception)
                {
                    // Best-effort cleanup; do not mask the original generation exception.
                }
#pragma warning restore RCS1075
#pragma warning restore CA1031
                throw;
            }
        }

        private static ChannelReader<FileWorkItem> CreateWorkChannel(long fileCount, int folders, DistributionType distribution, string fileType, int concurrency)
        {
            var channel = Channel.CreateBounded<FileWorkItem>(new BoundedChannelOptions(concurrency * 2)
            {
                FullMode = BoundedChannelFullMode.Wait,
            });
            var writer = channel.Writer;

            _ = Task.Run(async () =>
            {
                try
                {
                    for (long i = 1; i <= fileCount; i++)
                    {
                        var folderNumber = Distributions.GetFolderNumber(i, fileCount, folders, distribution);
                        var folderName = $"folder_{folderNumber:D3}";
                        var fileName = $"{i:D8}.{fileType}";
                        var filePathInZip = $"{folderName}/{fileName}";

                        await writer.WriteAsync(new FileWorkItem
                        {
                            Index = i,
                            FolderNumber = folderNumber,
                            FolderName = folderName,
                            FileName = fileName,
                            FilePathInZip = filePathInZip,
                        }).ConfigureAwait(false);
                    }

                    writer.Complete();
                }
                catch (Exception ex)
                {
                    writer.Complete(ex);
                }
            });

            return channel.Reader;
        }

        private async Task ProcessFileWorkAsync(ChannelReader<FileWorkItem> reader, long paddingPerFile, ChannelWriter<FileData> writer, FileGenerationRequest request, IFileGenerator fileGenerator)
        {
            long filesProcessed = 0;

            await foreach (var workItem in reader.ReadAllAsync().ConfigureAwait(false))
            {
                var fileData = this.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);
                try
                {
                    await writer.WriteAsync(fileData).ConfigureAwait(false);
                }
                catch
                {
                    fileData.MemoryOwner?.Dispose();
                    throw;
                }

                filesProcessed++;
                if (filesProcessed % PerformanceConstants.ProgressBatchSize == 0)
                {
                    this.performanceMonitor.ReportFilesCompleted(PerformanceConstants.ProgressBatchSize);
                }
            }

            // Report any remaining files
            if (filesProcessed % PerformanceConstants.ProgressBatchSize != 0)
            {
                var remainingFiles = filesProcessed % PerformanceConstants.ProgressBatchSize;
                this.performanceMonitor.ReportFilesCompleted(remainingFiles);
            }
        }

        internal FileData GenerateFileData(FileWorkItem workItem, long paddingPerFile, FileGenerationRequest request, IFileGenerator fileGenerator)
        {
            var generated = fileGenerator.Generate(workItem, request);
            var fileContent = generated.Content;
            var attachment = generated.Attachment;
            var pageCount = generated.PageCount;
            var email = generated.Email;

            var effectivePadding = paddingPerFile;

            // Cap the total size to the maximum allowed byte array size (2GB - 56 bytes) to prevent OutOfMemoryException
            const int maxByteArraySize = 2147483591;
            if (fileContent.Length + effectivePadding > maxByteArraySize)
            {
                effectivePadding = maxByteArraySize - fileContent.Length;
            }

            var totalSize = fileContent.Length + effectivePadding;

            var rentSize = (int)Math.Min(totalSize, PerformanceConstants.MaxPoolSize);
            var memoryOwner = totalSize > 0 && totalSize <= PerformanceConstants.MaxPoolSize
                ? MemoryPool<byte>.Shared.Rent(rentSize)
                : null;

            if (memoryOwner == null)
            {
                var data = new byte[(int)totalSize];
                Buffer.BlockCopy(fileContent, 0, data, 0, fileContent.Length);

                if (effectivePadding > 0)
                {
                    if (request.Metadata.Seed.HasValue)
                    {
                        var fileSeed = unchecked((int)(request.Metadata.Seed.Value + workItem.Index));
                        var rng = new DeterministicPaddingRng(fileSeed);
                        var paddingSpan = data.AsSpan(fileContent.Length, (int)effectivePadding);
                        rng.Fill(paddingSpan);
                    }
                    else
                    {
                        var padding = new byte[Math.Min(effectivePadding, 1024 * 1024)];
                        RandomNumberGenerator.Fill(padding);

                        int offset = fileContent.Length;
                        long remaining = effectivePadding;
                        while (remaining > 0)
                        {
                            int toCopy = (int)Math.Min(remaining, padding.Length);
                            Buffer.BlockCopy(padding, 0, data, offset, toCopy);
                            offset += toCopy;
                            remaining -= toCopy;
                        }
                    }
                }

#pragma warning disable S4790 // Cryptographic algorithms should be robust
                var hashBytes = MD5.HashData(data);
#pragma warning restore S4790
                var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                return new FileData
                {
                    WorkItem = workItem,
                    Data = data,
                    DataLength = (int)totalSize,
                    Attachment = attachment,
                    PageCount = pageCount,
                    Email = email,
                    Hash = hash,
                };
            }

            try
            {
                fileContent.CopyTo(memoryOwner.Memory.Span);

                if (effectivePadding > 0)
                {
                    var paddingSpan = memoryOwner.Memory.Span.Slice(fileContent.Length, (int)effectivePadding);
                    if (request.Metadata.Seed.HasValue)
                    {
                        var fileSeed = unchecked((int)(request.Metadata.Seed.Value + workItem.Index));
                        var rng = new DeterministicPaddingRng(fileSeed);
                        rng.Fill(paddingSpan);
                    }
                    else
                    {
                        RandomNumberGenerator.Fill(paddingSpan);
                    }
                }

                var finalMemory = memoryOwner.Memory[..(int)totalSize];
#pragma warning disable S4790 // Cryptographic algorithms should be robust
                var finalHashBytes = MD5.HashData(finalMemory.Span);
#pragma warning restore S4790
                var finalHash = Convert.ToHexString(finalHashBytes).ToLowerInvariant();

                return new FileData
                {
                    WorkItem = workItem,
                    Data = finalMemory,
                    DataLength = (int)totalSize,
                    MemoryOwner = memoryOwner,
                    Attachment = attachment,
                    PageCount = pageCount,
                    Email = email,
                    Hash = finalHash,
                };
            }
            catch
            {
                memoryOwner.Dispose();
                throw;
            }
        }

        internal long CalculatePaddingPerFile(long targetSize, int baseSize, long fileCount, bool withText)
        {
            var estimatedBaseSize = this.EstimateCompressedSize(baseSize, fileCount, withText);
            if (estimatedBaseSize >= targetSize)
            {
                throw new InvalidOperationException(
                    $"Estimated minimum compressed size ({estimatedBaseSize:N0} bytes) already exceeds " +
                    $"the target ZIP size ({targetSize:N0} bytes). Cannot proceed — reduce --count, " +
                    $"use smaller files, or increase --target-zip-size.");
            }

            var padding = (targetSize - estimatedBaseSize) / fileCount;
            return Math.Min(padding, PerformanceConstants.MaxPaddingPerFile);
        }

        private long EstimateCompressedSize(int contentSize, long count, bool withText)
        {
            // Simple estimation - in reality this would be more complex
            var baseSize = contentSize * 2; // Assume 50% compression
            if (withText)
            {
                baseSize += 50; // Text file overhead
            }

            return baseSize * count;
        }

        private static void RethrowIfNotNull(Exception? ex)
        {
            if (ex is not null)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        private struct DeterministicPaddingRng
        {
            private uint state;

            public DeterministicPaddingRng(int seed)
            {
                // Use a mixer to avoid poor randomness if seed is small
                uint s = (uint)seed;
                s ^= s >> 16;
                s *= 0x7feb352dU;
                s ^= s >> 15;
                s *= 0x846ca68bU;
                s ^= s >> 16;
                this.state = s == 0 ? 0x12345678U : s;
            }

            public void Fill(Span<byte> buffer)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    this.state = unchecked((this.state * 1664525U) + 1013904223U);
                    buffer[i] = (byte)(this.state >> 24);
                }
            }
        }
    }

    internal record FileWorkItem
    {
        public long Index { get; init; }

        public int FolderNumber { get; init; }

        public string FolderName { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string FilePathInZip { get; init; } = string.Empty;
    }

    internal record FileData
    {
        public FileWorkItem WorkItem { get; init; } = new FileWorkItem();

        public ReadOnlyMemory<byte> Data { get; init; } = ReadOnlyMemory<byte>.Empty;

        /// <summary>
        /// Length of Data, stored explicitly because MemoryOwner is disposed
        /// before load file generation. Access DataLength, not Data.Length,
        /// after MemoryOwner.Dispose().
        /// </summary>
        public int DataLength { get; init; }

        public (string filename, byte[] content)? Attachment { get; init; }

        public IMemoryOwner<byte>? MemoryOwner { get; init; }

        public int PageCount { get; init; } = 1;

        public Email? Email { get; init; }

        public string Hash { get; init; } = string.Empty;
    }
}
