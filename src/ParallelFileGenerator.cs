using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace Zipper
{
    /// <summary>
    /// Generates files in parallel with controlled concurrency and memory pooling.
    /// </summary>
    public class ParallelFileGenerator : IDisposable
    {
        private readonly PerformanceMonitor performanceMonitor = new PerformanceMonitor();

        public async Task<FileGenerationResult> GenerateFilesAsync(FileGenerationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Clone to avoid mutating the caller's request object
            request = request.Clone();

            this.performanceMonitor.Start(request.FileCount);

            try
            {
                // Validate inputs
                if (request.FileCount <= 0)
                {
                    throw new ArgumentException("File count must be positive", nameof(request.FileCount));
                }

                if (request.Concurrency <= 0)
                {
                    request.Concurrency = PerformanceConstants.DefaultConcurrency;
                }

                var fileGenerator = FileGeneratorFactory.Create(request.FileType, request)
                    ?? throw new InvalidOperationException($"Unknown file type: {request.FileType}");

                // For EML files, use sequential processing to avoid ZIP entry creation conflicts
                if (fileGenerator.RequiresSequentialProcessing(request))
                {
                    request.Concurrency = 1; // Force sequential processing
                }

                Directory.CreateDirectory(request.OutputPath);

                var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
                var zipFilePath = Path.Combine(request.OutputPath, $"{baseFileName}.zip");
                var loadFileName = $"{baseFileName}.dat";
                var loadFilePath = Path.Combine(request.OutputPath, loadFileName);

                var placeholderContent = fileGenerator.IsPlaceholderBased
                    ? PlaceholderFiles.GetContent(request.FileType.ToLowerInvariant())
                    : Array.Empty<byte>();

                long paddingPerFile = 0;
                if (request.TargetZipSize.HasValue)
                {
                    var baseSize = placeholderContent.Length > 0 ? placeholderContent.Length : 1024;
                    paddingPerFile = this.CalculatePaddingPerFile(request.TargetZipSize.Value, baseSize, request.FileCount, request.WithText);
                }

                // Create channels for work distribution
                var workChannelReader = CreateWorkChannel(request.FileCount, request.Folders, request.Distribution, request.FileType, request.Concurrency);
                var resultChannel = Channel.CreateBounded<FileData>(new BoundedChannelOptions(request.Concurrency * 2)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                });

                // Start the consumer task to write the archive concurrently
                var consumerTask = this.WriteArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, resultChannel.Reader);

                // Generate files in parallel (producers)
                using var semaphore = new SemaphoreSlim(request.Concurrency);
                var producerTasks = Enumerable.Range(0, request.Concurrency)
                    .Select(i => this.ProcessFileWorkAsync(semaphore, workChannelReader, paddingPerFile, resultChannel.Writer, request, fileGenerator))
                    .ToList();

                // Wait for all producers to complete, wrapped in a task that ensures completion
                var allProducersTask = Task.WhenAll(producerTasks);

                Exception? producerException = null;
                try
                {
                    await allProducersTask;
                }
                catch (Exception ex)
                {
                    // Capture exception — must await consumer before rethrowing
                    producerException = ex;
                }
                finally
                {
                    // Signal production done so consumer can drain and exit
                    resultChannel.Writer.Complete(null);
                }

                // Always wait for consumer (releases zip file handles)
                var actualLoadFilePath = await consumerTask;

                RethrowIfNotNull(producerException);

                this.performanceMonitor.FinalizeProgress();
                var performanceMetrics = this.performanceMonitor.Stop();

                return new FileGenerationResult
                {
                    ZipFilePath = zipFilePath,
                    LoadFilePath = actualLoadFilePath,
                    FilesGenerated = request.FileCount,
                    GenerationTime = TimeSpan.FromMilliseconds(performanceMetrics.ElapsedMilliseconds),
                    FilesPerSecond = performanceMetrics.FilesPerSecond,
                };
            }
            catch
            {
                this.performanceMonitor.Stop();
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
                        var folderNumber = FileDistributionHelper.GetFolderNumber(i, fileCount, folders, distribution);
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
                        });
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

        private async Task ProcessFileWorkAsync(SemaphoreSlim semaphore, ChannelReader<FileWorkItem> reader, long paddingPerFile, ChannelWriter<FileData> writer, FileGenerationRequest request, IFileGenerator fileGenerator)
        {
            long filesProcessed = 0;

            await foreach (var workItem in reader.ReadAllAsync())
            {
                await semaphore.WaitAsync();
                try
                {
                    var fileData = this.GenerateFileData(workItem, paddingPerFile, request, fileGenerator);
                    await writer.WriteAsync(fileData);

                    filesProcessed++;
                    if (filesProcessed % PerformanceConstants.ProgressBatchSize == 0)
                    {
                        this.performanceMonitor.ReportFilesCompleted(PerformanceConstants.ProgressBatchSize);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }

            // Report any remaining files
            if (filesProcessed % PerformanceConstants.ProgressBatchSize != 0)
            {
                var remainingFiles = filesProcessed % PerformanceConstants.ProgressBatchSize;
                this.performanceMonitor.ReportFilesCompleted(remainingFiles);
            }
        }

        private FileData GenerateFileData(FileWorkItem workItem, long paddingPerFile, FileGenerationRequest request, IFileGenerator fileGenerator)
        {
            var generated = fileGenerator.Generate(workItem, request);
            var fileContent = generated.Content;
            var attachment = generated.Attachment;
            var pageCount = generated.PageCount;
            var emailTemplate = generated.EmailTemplate;

            var effectivePadding = paddingPerFile;

            // Cap the total size to the maximum allowed byte array size (2GB - 56 bytes) to prevent OutOfMemoryException
            const int maxByteArraySize = 2147483591;
            if (fileContent.Length + effectivePadding > maxByteArraySize)
            {
                effectivePadding = maxByteArraySize - fileContent.Length;
            }

            var totalSize = fileContent.Length + effectivePadding;

            var rentSize = (int)Math.Min(totalSize, PerformanceConstants.MaxPoolSize);
            var memoryOwner = rentSize > 0 && rentSize <= PerformanceConstants.MaxPoolSize
                ? MemoryPool<byte>.Shared.Rent(rentSize)
                : null;

            if (memoryOwner == null)
            {
                var data = new byte[(int)totalSize];
                Buffer.BlockCopy(fileContent, 0, data, 0, fileContent.Length);

                if (paddingPerFile > 0)
                {
                    var padding = new byte[Math.Min(paddingPerFile, 1024 * 1024)];
                    RandomNumberGenerator.Fill(padding);

                    int offset = fileContent.Length;
                    long remaining = paddingPerFile;
                    while (remaining > 0)
                    {
                        int toCopy = (int)Math.Min(remaining, padding.Length);
                        Buffer.BlockCopy(padding, 0, data, offset, toCopy);
                        offset += toCopy;
                        remaining -= toCopy;
                    }
                }

                return new FileData
                {
                    WorkItem = workItem,
                    Data = data,
                    DataLength = (int)totalSize,
                    Attachment = attachment,
                    PageCount = pageCount,
                    EmailTemplate = emailTemplate,
                };
            }

            fileContent.CopyTo(memoryOwner.Memory.Span);

            if (paddingPerFile > 0)
            {
                var paddingSpan = memoryOwner.Memory.Span.Slice(fileContent.Length, (int)paddingPerFile);
                RandomNumberGenerator.Fill(paddingSpan);
            }

            return new FileData
            {
                WorkItem = workItem,
                Data = memoryOwner.Memory[..(int)totalSize],
                DataLength = (int)totalSize,
                MemoryOwner = memoryOwner,
                Attachment = attachment,
                PageCount = pageCount,
                EmailTemplate = emailTemplate,
            };
        }

        private async Task<string> WriteArchiveAsync(string zipFilePath, string loadFileName, string loadFilePath, FileGenerationRequest request, ChannelReader<FileData> resultReader)
        {
            // Delegate to new ZipArchiveService for actual ZIP creation
            return await ZipArchiveService.CreateArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, resultReader);
        }

        private long CalculatePaddingPerFile(long targetSize, int baseSize, long fileCount, bool withText)
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

        public void Dispose()
        {
        }

        private static void RethrowIfNotNull(Exception? ex)
        {
            if (ex is not null)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
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

        public EmailTemplate? EmailTemplate { get; init; }
    }
}
