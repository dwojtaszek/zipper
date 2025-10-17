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
        private readonly PerformanceMonitor _performanceMonitor = new PerformanceMonitor();

        public ParallelFileGenerator()
        {
            _memoryPoolManager = new MemoryPoolManager();
        }

        public async Task<FileGenerationResult> GenerateFilesAsync(FileGenerationRequest request)
        {
            _performanceMonitor.Start(request.FileCount);

            try
            {
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
                var workChannel = CreateWorkChannel(request.FileCount, request.Folders, request.Distribution, request.FileType);
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
                await WriteArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, resultChannel.Reader);

                var performanceMetrics = _performanceMonitor.Stop();
                Console.WriteLine(); // New line after progress

                return new FileGenerationResult
                {
                    ZipFilePath = zipFilePath,
                    LoadFilePath = loadFilePath,
                    FilesGenerated = request.FileCount,
                    GenerationTime = TimeSpan.FromMilliseconds(performanceMetrics.ElapsedMilliseconds),
                    FilesPerSecond = performanceMetrics.FilesPerSecond
                };
            }
            finally
            {
                _performanceMonitor.Stop();
            }
        }

        private Channel<FileWorkItem> CreateWorkChannel(long fileCount, int folders, DistributionType distribution, string fileType)
        {
            var channel = Channel.CreateUnbounded<FileWorkItem>();

            for (long i = 1; i <= fileCount; i++)
            {
                var folderNumber = FileDistributionHelper.GetFolderNumber(i, fileCount, folders, distribution);
                var folderName = $"folder_{folderNumber:D3}";
                var fileName = $"{i:D8}.{fileType}";
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
            long filesProcessed = 0;

            await foreach (var workItem in reader.ReadAllAsync())
            {
                await semaphore.WaitAsync();
                try
                {
                    var fileData = GenerateFileData(workItem, placeholderContent, paddingPerFile);
                    await writer.WriteAsync(fileData);

                    filesProcessed++;
                    if (filesProcessed % PerformanceConstants.ProgressBatchSize == 0)
                    {
                        _performanceMonitor.ReportFilesCompleted(PerformanceConstants.ProgressBatchSize);
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
                _performanceMonitor.ReportFilesCompleted(filesProcessed % PerformanceConstants.ProgressBatchSize);
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

        private async Task WriteArchiveAsync(string zipFilePath, string loadFileName, string loadFilePath, FileGenerationRequest request, ChannelReader<FileData> resultReader)
        {
            using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

            // Process generated files and write to archive
            await foreach (var fileData in resultReader.ReadAllAsync())
            {
                // Write main file
                var entry = archive.CreateEntry(fileData.WorkItem.FilePathInZip, CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                {
                    await entryStream.WriteAsync(fileData.Data);
                }

                // Write extracted text file if requested
                if (request.WithText)
                {
                    var textFileName = fileData.WorkItem.FileName.Replace($".{request.FileType}", ".txt");
                    var textFilePathInZip = $"{fileData.WorkItem.FolderName}/{textFileName}";
                    var textEntry = archive.CreateEntry(textFilePathInZip, CompressionLevel.Optimal);
                    using (var textEntryStream = textEntry.Open())
                    {
                        var textContent = GetExtractedTextContent(request.FileType);
                        await textEntryStream.WriteAsync(textContent);
                    }
                }

                fileData.MemoryOwner?.Dispose();
            }

            // Write load file
            if (request.IncludeLoadFile)
            {
                await WriteLoadFileToArchiveAsync(archive, loadFileName, request);
            }
            else
            {
                await WriteLoadFileToDiskAsync(loadFilePath, request);
            }
        }

        private async Task WriteLoadFileToArchiveAsync(ZipArchive archive, string loadFileName, FileGenerationRequest request)
        {
            var loadFileEntry = archive.CreateEntry(loadFileName, CompressionLevel.Optimal);
            using var loadFileStream = loadFileEntry.Open();
            using var writer = new StreamWriter(loadFileStream, Encoding.UTF8);

            await WriteLoadFileContent(writer, request);
        }

        private async Task WriteLoadFileToDiskAsync(string loadFilePath, FileGenerationRequest request)
        {
            using var fileStream = new FileStream(loadFilePath, FileMode.Create);
            using var writer = new StreamWriter(fileStream, GetEncoding(request.Encoding));

            await WriteLoadFileContent(writer, request);
        }

        private async Task WriteLoadFileContent(StreamWriter writer, FileGenerationRequest request)
        {
            const char colDelim = (char)20;
            const char quote = (char)254;

            // Build header based on options
            var headerBuilder = new System.Text.StringBuilder();
            headerBuilder.Append($"{quote}Control Number{quote}{colDelim}{quote}File Path{quote}");

            if (request.WithMetadata || request.FileType.ToLower() == "eml")
            {
                headerBuilder.Append($"{colDelim}{quote}Custodian{quote}{colDelim}{quote}Date Sent{quote}{colDelim}{quote}Author{quote}{colDelim}{quote}File Size{quote}");
            }

            if (request.FileType.ToLower() == "eml")
            {
                headerBuilder.Append($"{colDelim}{quote}To{quote}{colDelim}{quote}From{quote}{colDelim}{quote}Subject{quote}{colDelim}{quote}Sent Date{quote}{colDelim}{quote}Attachment{quote}");
            }

            if (request.WithText)
            {
                headerBuilder.Append($"{colDelim}{quote}Extracted Text{quote}");
            }

            await writer.WriteLineAsync(headerBuilder.ToString());

            // Write file records
            for (long i = 1; i <= request.FileCount; i++)
            {
                var folderNumber = FileDistributionHelper.GetFolderNumber(i, request.FileCount, request.Folders, request.Distribution);
                var folderName = $"folder_{folderNumber:D3}";
                var fileName = $"{i:D8}.{request.FileType}";
                var filePathInZip = $"{folderName}/{fileName}";
                var docId = $"DOC{i:D8}";

                var lineBuilder = new System.Text.StringBuilder();
                lineBuilder.Append($"{quote}{docId}{quote}{colDelim}{quote}{filePathInZip}{quote}");

                if (request.WithMetadata || request.FileType.ToLower() == "eml")
                {
                    var custodian = $"Custodian {folderNumber}";
                    var dateSent = DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                    var author = $"Author {Random.Shared.Next(1, 100):D3}";
                    var fileSize = 1024 + Random.Shared.Next(0, 2048); // Simulated file size

                    lineBuilder.Append($"{colDelim}{quote}{custodian}{quote}{colDelim}{quote}{dateSent}{quote}{colDelim}{quote}{author}{quote}{colDelim}{quote}{fileSize}{quote}");
                }

                if (request.FileType.ToLower() == "eml")
                {
                    var to = $"recipient{i}@example.com";
                    var from = $"sender{i}@example.com";
                    var subject = $"Email Subject {i}";
                    var sentDate = DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
                    var hasAttachment = Random.Shared.NextDouble() < (request.AttachmentRate / 100.0);
                    var attachment = hasAttachment ? $"attachment{i:D8}.pdf" : "";

                    lineBuilder.Append($"{colDelim}{quote}{to}{quote}{colDelim}{quote}{from}{quote}{colDelim}{quote}{subject}{quote}{colDelim}{quote}{sentDate}{quote}{colDelim}{quote}{attachment}{quote}");
                }

                if (request.WithText)
                {
                    var textFilePath = filePathInZip.Replace($".{request.FileType}", ".txt");
                    lineBuilder.Append($"{colDelim}{quote}{textFilePath}{quote}");
                }

                await writer.WriteLineAsync(lineBuilder.ToString());
            }
        }

        private Encoding GetEncoding(string encodingName)
        {
            return encodingName?.ToUpperInvariant() switch
            {
                "UTF-8" => new UTF8Encoding(false),
                "ANSI" => CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? new UTF8Encoding(false),
                "UTF-16" => new UnicodeEncoding(false, false),
                "UNICODE" => new UnicodeEncoding(false, false), // Handle .NET's EncodingName for UTF-16
                "WESTERN EUROPEAN (WINDOWS)" => CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? new UTF8Encoding(false), // Handle .NET's EncodingName for ANSI
                _ => new UTF8Encoding(false)
            };
        }

        private byte[] GetExtractedTextContent(string fileType)
        {
            return fileType.ToLower() switch
            {
                "eml" => PlaceholderFiles.EmlExtractedText,
                _ => PlaceholderFiles.ExtractedText
            };
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
