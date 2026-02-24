using System.Buffers;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace Zipper
{
    /// <summary>
    /// Generates files in parallel with controlled concurrency and memory pooling.
    /// </summary>
    public class ParallelFileGenerator : IDisposable
    {
        private readonly MemoryPoolManager memoryPoolManager;
        private readonly PerformanceMonitor performanceMonitor = new PerformanceMonitor();

        public ParallelFileGenerator()
        {
            this.memoryPoolManager = new MemoryPoolManager();
        }

        public async Task<FileGenerationResult> GenerateFilesAsync(FileGenerationRequest request)
        {
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

                // For EML files, use sequential processing to avoid ZIP entry creation conflicts
                // when attachments and text extraction are enabled
                if (request.FileType.ToLowerInvariant() == "eml" && (request.WithText || request.AttachmentRate > 0))
                {
                    request.Concurrency = 1; // Force sequential processing
                }

                Directory.CreateDirectory(request.OutputPath);

                var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
                var zipFilePath = Path.Combine(request.OutputPath, $"{baseFileName}.zip");
                var loadFileName = $"{baseFileName}.dat";
                var loadFilePath = Path.Combine(request.OutputPath, loadFileName);

                var placeholderContent = PlaceholderFiles.GetContent(request.FileType.ToLowerInvariant());

                // Allow Office formats and EML to have empty placeholder content (generated dynamically)
                if (placeholderContent.Length == 0 &&
                    request.FileType.ToLowerInvariant() != "eml" &&
                    !OfficeFileGenerator.IsOfficeFormat(request.FileType))
                {
                    throw new InvalidOperationException($"Unknown file type: {request.FileType}");
                }

                long paddingPerFile = 0;
                if (request.TargetZipSize.HasValue)
                {
                    paddingPerFile = this.CalculatePaddingPerFile(request.TargetZipSize.Value, placeholderContent.Length, request.FileCount, request.WithText);
                }

                // Create channels for work distribution
                var workChannelReader = CreateWorkChannel(request.FileCount, request.Folders, request.Distribution, request.FileType);
                var resultChannel = Channel.CreateBounded<FileData>(new BoundedChannelOptions(request.Concurrency * 2)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                });

                // Start the consumer task to write the archive concurrently
                var consumerTask = this.WriteArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, resultChannel.Reader);

                // Generate files in parallel (producers)
                using var semaphore = new SemaphoreSlim(request.Concurrency);
                var producerTasks = Enumerable.Range(0, request.Concurrency)
                    .Select(i => this.ProcessFileWorkAsync(semaphore, workChannelReader, placeholderContent, paddingPerFile, resultChannel.Writer, request))
                    .ToList();

                // Wait for all producers to complete, wrapped in a task that ensures completion
                var allProducersTask = Task.WhenAll(producerTasks);

                try
                {
                    await allProducersTask;
                }
                finally
                {
                    // Signal that production is done, even if producers fail
                    resultChannel.Writer.Complete(allProducersTask.Exception);
                }

                // Wait for the consumer to finish writing the archive
                var actualLoadFilePath = await consumerTask;

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

        private static ChannelReader<FileWorkItem> CreateWorkChannel(long fileCount, int folders, DistributionType distribution, string fileType)
        {
            var channel = Channel.CreateUnbounded<FileWorkItem>();
            var writer = channel.Writer;

            Task.Run(async () =>
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
            });

            return channel.Reader;
        }

        private async Task ProcessFileWorkAsync(SemaphoreSlim semaphore, ChannelReader<FileWorkItem> reader, byte[] placeholderContent, long paddingPerFile, ChannelWriter<FileData> writer, FileGenerationRequest request)
        {
            long filesProcessed = 0;

            await foreach (var workItem in reader.ReadAllAsync())
            {
                await semaphore.WaitAsync();
                try
                {
                    var fileData = this.GenerateFileData(workItem, placeholderContent, paddingPerFile, request);
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

        private FileData GenerateFileData(FileWorkItem workItem, byte[] placeholderContent, long paddingPerFile, FileGenerationRequest request)
        {
            byte[] fileContent;
            (string filename, byte[] content)? attachment = null;
            int pageCount = 1;

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                // Use the new EmlGenerationService for clean separation of concerns
                var emlResult = EmlGenerationService.GenerateEmlContent(
                    (int)workItem.Index,
                    request.AttachmentRate);

                fileContent = emlResult.Content;
                attachment = emlResult.Attachment;
            }
            else if (OfficeFileGenerator.IsOfficeFormat(request.FileType))
            {
                // Generate Office format documents
                fileContent = OfficeFileGenerator.GenerateContent(request.FileType, workItem);
            }
            else if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
            {
                // Generate multipage TIFF
                pageCount = TiffMultiPageGenerator.GetPageCount(request.TiffPageRange, workItem.Index);
                fileContent = TiffMultiPageGenerator.Generate(pageCount, workItem);
            }
            else
            {
                fileContent = placeholderContent;
            }

            var totalSize = fileContent.Length + paddingPerFile;

            // Cap the total size to the maximum allowed byte array size (2GB - 56 bytes) to prevent OutOfMemoryException
            const int maxByteArraySize = 2147483591;
            if (totalSize > maxByteArraySize)
            {
                totalSize = maxByteArraySize;
                paddingPerFile = totalSize - fileContent.Length;
            }

            var memoryOwner = this.memoryPoolManager.Rent((int)Math.Min(totalSize, PerformanceConstants.MaxPoolSize));
            if (memoryOwner == null)
            {
                // Fallback to direct allocation for very large files, cast is now safe due to the cap above
                var data = new byte[(int)totalSize];
                Buffer.BlockCopy(fileContent, 0, data, 0, fileContent.Length);

                if (paddingPerFile > 0)
                {
                    var padding = new byte[Math.Min(paddingPerFile, 1024 * 1024)]; // Max 1MB padding chunks
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
                    Attachment = attachment,
                    PageCount = pageCount,
                };
            }

            // Use pooled memory
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
                MemoryOwner = memoryOwner,
                Attachment = attachment,
                PageCount = pageCount,
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
                return 0;
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
            this.memoryPoolManager?.Dispose();
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

        public (string filename, byte[] content)? Attachment { get; init; }

        public IMemoryOwner<byte>? MemoryOwner { get; init; }

        public int PageCount { get; init; } = 1;
    }
}
