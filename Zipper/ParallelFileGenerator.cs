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
            ProgressTracker.Initialize(request.FileCount);

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
                if (placeholderContent.Length == 0 && request.FileType.ToLower() != "eml")
                    throw new InvalidOperationException($"Unknown file type: {request.FileType}");

                long paddingPerFile = 0;
                if (request.TargetZipSize.HasValue)
                {
                    paddingPerFile = CalculatePaddingPerFile(request.TargetZipSize.Value, placeholderContent.Length, request.FileCount, request.WithText);
                }

                // Create channels for work distribution
                var workChannel = CreateWorkChannel(request.FileCount, request.Folders, request.Distribution, request.FileType);
                var resultChannel = Channel.CreateBounded<FileData>(new BoundedChannelOptions(request.Concurrency * 2)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

                // Start the consumer task to write the archive concurrently
                var consumerTask = WriteArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, resultChannel.Reader);

                // Generate files in parallel (producers)
                using var semaphore = new SemaphoreSlim(request.Concurrency);
                var producerTasks = Enumerable.Range(0, request.Concurrency)
                    .Select(i => ProcessFileWorkAsync(semaphore, workChannel.Reader, placeholderContent, paddingPerFile, resultChannel.Writer, request));

                // Wait for all producers to complete
                await Task.WhenAll(producerTasks);

                // Signal that production is done
                resultChannel.Writer.Complete();

                // Wait for the consumer to finish writing the archive
                await consumerTask;

                var performanceMetrics = _performanceMonitor.Stop();
                ProgressTracker.FinalizeProgress();

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

        private async Task ProcessFileWorkAsync(SemaphoreSlim semaphore, ChannelReader<FileWorkItem> reader, byte[] placeholderContent, long paddingPerFile, ChannelWriter<FileData> writer, FileGenerationRequest request)
        {
            long filesProcessed = 0;

            await foreach (var workItem in reader.ReadAllAsync())
            {
                await semaphore.WaitAsync();
                try
                {
                    var fileData = GenerateFileData(workItem, placeholderContent, paddingPerFile, request);
                    await writer.WriteAsync(fileData);

                    filesProcessed++;
                    if (filesProcessed % PerformanceConstants.ProgressBatchSize == 0)
                    {
                        ProgressTracker.ReportFilesCompleted(PerformanceConstants.ProgressBatchSize);
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
                var remainingFiles = filesProcessed % PerformanceConstants.ProgressBatchSize;
                ProgressTracker.ReportFilesCompleted(remainingFiles);
                _performanceMonitor.ReportFilesCompleted(remainingFiles);
            }
        }

        private FileData GenerateFileData(FileWorkItem workItem, byte[] placeholderContent, long paddingPerFile, FileGenerationRequest request)
        {
            byte[] fileContent;
            (string filename, byte[] content)? attachment = null;

            if (request.FileType.ToLower() == "eml")
            {
                // Use the new EmailTemplateSystem for realistic email generation
                var emailTemplate = EmailTemplateSystem.GetRandomTemplate((int)workItem.Index, (int)workItem.Index);

                if (Random.Shared.Next(100) < request.AttachmentRate)
                {
                    attachment = PlaceholderFiles.GetRandomAttachment();
                }

                fileContent = EmailBuilder.BuildEmail(emailTemplate.To, emailTemplate.From, emailTemplate.Subject, emailTemplate.SentDate, emailTemplate.Body, attachment);
            }
            else
            {
                fileContent = placeholderContent;
            }

            var totalSize = fileContent.Length + paddingPerFile;

            var memoryOwner = _memoryPoolManager.Rent((int)Math.Min(totalSize, PerformanceConstants.MaxPoolSize));
            if (memoryOwner == null)
            {
                // Fallback to direct allocation for very large files
                var data = new byte[totalSize];
                Buffer.BlockCopy(fileContent, 0, data, 0, fileContent.Length);

                if (paddingPerFile > 0)
                {
                    var padding = new byte[paddingPerFile];
                    RandomNumberGenerator.Fill(padding);
                    Buffer.BlockCopy(padding, 0, data, fileContent.Length, padding.Length);
                }

                return new FileData { WorkItem = workItem, Data = data, Attachment = attachment };
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
                Data = memoryOwner.Memory[..(int)totalSize].ToArray(),
                MemoryOwner = memoryOwner,
                Attachment = attachment
            };
        }

        private static string GetContentTypeForExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".doc" or ".docx" => "application/msword",
                ".xls" or ".xlsx" => "application/vnd.ms-excel",
                ".txt" => "text/plain",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
        }

        private async Task WriteArchiveAsync(string zipFilePath, string loadFileName, string loadFilePath, FileGenerationRequest request, ChannelReader<FileData> resultReader)
        {
            using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);
            var processedFiles = new ConcurrentBag<FileData>();

            // Process generated files and write to archive
            await foreach (var fileData in resultReader.ReadAllAsync())
            {
                processedFiles.Add(fileData);

                // Write main file
                var entry = archive.CreateEntry(fileData.WorkItem.FilePathInZip, CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                {
                    await entryStream.WriteAsync(fileData.Data);
                }

                // Write attachment if it exists (for EML)
                if (fileData.Attachment.HasValue)
                {
                    var attachmentEntry = archive.CreateEntry($"{fileData.WorkItem.FolderName}/{fileData.Attachment.Value.filename}", CompressionLevel.Optimal);
                    using (var attachmentStream = attachmentEntry.Open())
                    {
                        await attachmentStream.WriteAsync(fileData.Attachment.Value.content);
                    }

                    if (request.WithText)
                    {
                        var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(fileData.Attachment.Value.filename)}.txt";
                        var attachmentTextEntry = archive.CreateEntry($"{fileData.WorkItem.FolderName}/{attachmentTextFileName}", CompressionLevel.Optimal);
                        using (var attachmentTextStream = attachmentTextEntry.Open())
                        {
                            await attachmentTextStream.WriteAsync(PlaceholderFiles.ExtractedText);
                        }
                    }
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
                await WriteLoadFileToArchiveAsync(archive, loadFileName, request, processedFiles.ToList());
            }
            else
            {
                await WriteLoadFileToDiskAsync(loadFilePath, request, processedFiles.ToList());
            }
        }

        private async Task WriteLoadFileToArchiveAsync(ZipArchive archive, string loadFileName, FileGenerationRequest request, List<FileData> processedFiles)
        {
            var loadFileEntry = archive.CreateEntry(loadFileName, CompressionLevel.Optimal);
            using var loadFileStream = loadFileEntry.Open();
            using var writer = new StreamWriter(loadFileStream, Encoding.UTF8);

            await WriteLoadFileContent(writer, request, processedFiles);
        }

        private async Task WriteLoadFileToDiskAsync(string loadFilePath, FileGenerationRequest request, List<FileData> processedFiles)
        {
            using var fileStream = new FileStream(loadFilePath, FileMode.Create);
            using var writer = new StreamWriter(fileStream, GetEncoding(request.Encoding));

            await WriteLoadFileContent(writer, request, processedFiles);
        }

        private async Task WriteLoadFileContent(StreamWriter writer, FileGenerationRequest request, List<FileData> processedFiles)
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
            foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
            {
                var workItem = fileData.WorkItem;
                var docId = $"DOC{workItem.Index:D8}";

                var lineBuilder = new System.Text.StringBuilder();
                lineBuilder.Append($"{quote}{docId}{quote}{colDelim}{quote}{workItem.FilePathInZip}{quote}");

                if (request.WithMetadata || request.FileType.ToLower() == "eml")
                {
                    var custodian = $"Custodian {workItem.FolderNumber}";
                    var dateSent = DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                    var author = $"Author {Random.Shared.Next(1, 100):D3}";
                    var fileSize = fileData.Data.Length;

                    lineBuilder.Append($"{colDelim}{quote}{custodian}{quote}{colDelim}{quote}{dateSent}{quote}{colDelim}{quote}{author}{quote}{colDelim}{quote}{fileSize}{quote}");
                }

                if (request.FileType.ToLower() == "eml")
                {
                    var to = $"recipient{workItem.Index}@example.com";
                    var from = $"sender{workItem.Index}@example.com";
                    var subject = $"Email Subject {workItem.Index}";
                    var sentDate = DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
                    var attachmentName = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : "";

                    lineBuilder.Append($"{colDelim}{quote}{to}{quote}{colDelim}{quote}{from}{quote}{colDelim}{quote}{subject}{quote}{colDelim}{quote}{sentDate}{quote}{colDelim}{quote}{attachmentName}{quote}");
                }

                if (request.WithText)
                {
                    var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
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
        public (string filename, byte[] content)? Attachment { get; init; }
        public IMemoryOwner<byte>? MemoryOwner { get; init; }
    }
}
