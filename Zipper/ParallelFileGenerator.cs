using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Zipper
{
    /// <summary>
    /// Generates files in parallel with controlled concurrency and memory pooling
    /// </summary>
    public class ParallelFileGenerator : IDisposable
    {
        private readonly MemoryPoolManager _memoryPoolManager;

        public ParallelFileGenerator()
        {
            _memoryPoolManager = new MemoryPoolManager();
        }

        public async Task<FileGenerationResult> GenerateFilesAsync(FileGenerationRequest request)
        {
            var startTime = DateTime.UtcNow;

            // Validate inputs
            if (request.FileCount <= 0)
                throw new ArgumentException("File count must be positive", nameof(request.FileCount));

            if (request.Concurrency <= 0)
                request.Concurrency = PerformanceConstants.DefaultConcurrency;

            Directory.CreateDirectory(request.OutputPath);

            var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
            var zipFilePath = Path.Combine(request.OutputPath, $"{baseFileName}.zip");
            var loadFileName = $"{baseFileName}.dat";
            var loadFilePath = Path.Combine(request.OutputPath, loadFileName);

            var placeholderContent = PlaceholderFiles.GetContent(request.FileType.ToLower());
            if (placeholderContent.Length == 0)
                throw new InvalidOperationException($"Unknown file type: {request.FileType}");

            long paddingPerFile = 0;
            if (request.TargetZipSize.HasValue)
            {
                paddingPerFile = CalculatePaddingPerFile(request.TargetZipSize.Value, placeholderContent.Length, request.FileCount, request.WithText);
            }

            // Create channels for work distribution
            var workChannel = CreateWorkChannel(request.FileCount, request.Folders, request.Distribution);
            var resultChannel = Channel.CreateUnbounded<FileData>();

            // Generate files in parallel
            using var semaphore = new SemaphoreSlim(request.Concurrency);
            var tasks = Enumerable.Range(0, request.Concurrency)
                .Select(i => ProcessFileWorkAsync(semaphore, workChannel.Reader, placeholderContent, paddingPerFile, resultChannel.Writer));

            var fileGenerationTasks = Task.WhenAll(tasks);

            // Wait for all file generation to complete
            await fileGenerationTasks;
            resultChannel.Writer.Complete();

            // Process results and write to archive
            await WriteArchiveAsync(zipFilePath, loadFileName, request, resultChannel.Reader);

            var generationTime = DateTime.UtcNow - startTime;
            var rate = generationTime.TotalSeconds > 0 ? request.FileCount / generationTime.TotalSeconds : 0;

            return new FileGenerationResult
            {
                ZipFilePath = zipFilePath,
                LoadFilePath = loadFilePath,
                FilesGenerated = request.FileCount,
                GenerationTime = generationTime,
                FilesPerSecond = rate
            };
        }

        private Channel<FileWorkItem> CreateWorkChannel(long fileCount, int folders, DistributionType distribution)
        {
            var channel = Channel.CreateUnbounded<FileWorkItem>();

            for (long i = 1; i <= fileCount; i++)
            {
                var folderNumber = FileDistributionHelper.GetFolderNumber(i, fileCount, folders, distribution);
                var folderName = $"folder_{folderNumber:D3}";
                var fileName = $"{i:D8}.pdf"; // Assuming PDF for now
                var filePathInZip = $"{folderName}/{fileName}";

                channel.Writer.WriteAsync(new FileWorkItem
                {
                    Index = i,
                    FolderNumber = folderNumber,
                    FolderName = folderName,
                    FileName = fileName,
                    FilePathInZip = filePathInZip
                });
            }

            channel.Writer.Complete();
            return channel;
        }

        private async Task ProcessFileWorkAsync(SemaphoreSlim semaphore, ChannelReader<FileWorkItem> reader, byte[] placeholderContent, long paddingPerFile, ChannelWriter<FileData> writer)
        {
            await foreach (var workItem in reader.ReadAllAsync())
            {
                await semaphore.WaitAsync();
                try
                {
                    var fileData = GenerateFileData(workItem, placeholderContent, paddingPerFile);
                    await writer.WriteAsync(fileData);
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }

        private FileData GenerateFileData(FileWorkItem workItem, byte[] placeholderContent, long paddingPerFile)
        {
            var totalSize = placeholderContent.Length + paddingPerFile;

            var memoryOwner = _memoryPoolManager.Rent((int)Math.Min(totalSize, PerformanceConstants.MaxPoolSize));
            if (memoryOwner == null)
            {
                // Fallback to direct allocation for very large files
                var data = new byte[totalSize];
                Buffer.BlockCopy(placeholderContent, 0, data, 0, placeholderContent.Length);

                if (paddingPerFile > 0)
                {
                    var padding = new byte[paddingPerFile];
                    RandomNumberGenerator.Fill(padding);
                    Buffer.BlockCopy(padding, 0, data, placeholderContent.Length, padding.Length);
                }

                return new FileData { WorkItem = workItem, Data = data };
            }

            // Use pooled memory
            placeholderContent.CopyTo(memoryOwner.Memory.Span);

            if (paddingPerFile > 0)
            {
                var paddingSpan = memoryOwner.Memory.Span.Slice(placeholderContent.Length, (int)paddingPerFile);
                RandomNumberGenerator.Fill(paddingSpan);
            }

            return new FileData
            {
                WorkItem = workItem,
                Data = memoryOwner.Memory[..(int)totalSize].ToArray(),
                MemoryOwner = memoryOwner
            };
        }

        private async Task WriteArchiveAsync(string zipFilePath, string loadFileName, FileGenerationRequest request, ChannelReader<FileData> resultReader)
        {
            using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

            // Process generated files and write to archive
            await foreach (var fileData in resultReader.ReadAllAsync())
            {
                var entry = archive.CreateEntry(fileData.WorkItem.FilePathInZip, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                await entryStream.WriteAsync(fileData.Data);

                fileData.MemoryOwner?.Dispose();
            }

            // Write load file
            await WriteLoadFileAsync(archive, loadFileName, request);
        }

        private async Task WriteLoadFileAsync(ZipArchive archive, string loadFileName, FileGenerationRequest request)
        {
            var loadFileEntry = archive.CreateEntry(loadFileName, CompressionLevel.Optimal);
            using var loadFileStream = loadFileEntry.Open();
            using var writer = new StreamWriter(loadFileStream, Encoding.UTF8);

            const char colDelim = (char)20;
            const char quote = (char)254;

            var header = $"{quote}Control Number{quote}{colDelim}{quote}File Path{quote}";
            await writer.WriteLineAsync(header);

            for (long i = 1; i <= request.FileCount; i++)
            {
                var folderNumber = FileDistributionHelper.GetFolderNumber(i, request.FileCount, request.Folders, request.Distribution);
                var folderName = $"folder_{folderNumber:D3}";
                var fileName = $"{i:D8}.{request.FileType}";
                var filePathInZip = $"{folderName}/{fileName}";
                var docId = $"DOC{i:D8}";

                var line = $"{quote}{docId}{quote}{colDelim}{quote}{filePathInZip}{quote}";
                await writer.WriteLineAsync(line);
            }
        }

        private long CalculatePaddingPerFile(long targetSize, int baseSize, long fileCount, bool withText)
        {
            var estimatedBaseSize = EstimateCompressedSize(baseSize, fileCount, withText);
            if (estimatedBaseSize >= targetSize)
                return 0;

            var padding = (targetSize - estimatedBaseSize) / fileCount;
            return Math.Min(padding, PerformanceConstants.MaxPaddingPerFile);
        }

        private long EstimateCompressedSize(int contentSize, long count, bool withText)
        {
            // Simple estimation - in reality this would be more complex
            var baseSize = contentSize * 2; // Assume 50% compression
            if (withText)
                baseSize += 50; // Text file overhead

            return baseSize * count;
        }

        public void Dispose()
        {
            _memoryPoolManager?.Dispose();
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
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public IMemoryOwner<byte>? MemoryOwner { get; init; }
    }
}