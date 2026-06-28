using System.Text;
using System.Text.Json;
using Xunit;

using Zipper.Config;
using Zipper.LoadFiles;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zipper
{
    [Collection("Sequential")]
    public class LoadfileOnlyGeneratorTests : IDisposable
    {
        private readonly string tempDir;

        public LoadfileOnlyGeneratorTests()
        {
            this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(this.tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, true);
            }
        }

        private FileGenerationRequest CreateRequest(LoadFileFormat format = LoadFileFormat.Dat, long count = 10)
        {
            return new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = this.tempDir,
                    FileCount = count,
                    FileType = "pdf",
                },
                LoadFile = new LoadFileConfig
                {
                    LoadFileFormat = format,
                    Encoding = "UTF-8",
                },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
                Metadata = new MetadataConfig { Seed = 42 },
                LoadfileOnly = true,
            };
        }

        [Fact]
        public async Task GenerateAsync_Dat_CreatesLoadFileWithCorrectLineCount()
        {
            var request = this.CreateRequest(count: 50);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.True(File.Exists(result.LoadFilePath));
            var lines = await File.ReadAllLinesAsync(result.LoadFilePath);

            // 1 header + 50 data rows
            Assert.Equal(51, lines.Length);
        }

        [Fact]
        public async Task GenerateAsync_Dat_HeaderContainsExpectedColumns()
        {
            var request = this.CreateRequest();
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            var firstLine = (await File.ReadAllLinesAsync(result.LoadFilePath))[0];
            Assert.Contains("Control Number", firstLine, StringComparison.Ordinal);
            Assert.Contains("File Path", firstLine, StringComparison.Ordinal);
            Assert.Contains("Custodian", firstLine, StringComparison.Ordinal);
            Assert.Contains("EmailSubject", firstLine, StringComparison.Ordinal);
            Assert.Contains("ExtractedText", firstLine, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_Dat_UsesConfiguredDelimiters()
        {
            var request = this.CreateRequest();
            request.Delimiters = request.Delimiters with { ColumnDelimiter = "\u0014" };
            request.Delimiters = request.Delimiters with { QuoteDelimiter = "\u00fe" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var content = await File.ReadAllTextAsync(result.LoadFilePath);

            Assert.Contains('\u0014', content);
            Assert.Contains('\u00fe', content);
        }

        [Fact]
        public async Task GenerateAsync_Dat_WithQuoteDelimNone_OmitsQuotes()
        {
            var request = this.CreateRequest();
            request.Delimiters = request.Delimiters with { QuoteDelimiter = string.Empty };
            request.Delimiters = request.Delimiters with { ColumnDelimiter = "|" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var firstLine = (await File.ReadAllLinesAsync(result.LoadFilePath))[0];

            Assert.DoesNotContain('\u00fe', firstLine);
            Assert.Contains('|', firstLine);
        }

        [Fact]
        public async Task GenerateAsync_Opt_CreatesOpticonFormat()
        {
            var request = this.CreateRequest(LoadFileFormat.Opt, 20);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.True(File.Exists(result.LoadFilePath));
            var lines = await File.ReadAllLinesAsync(result.LoadFilePath);

            // OPT has no header, just data rows (20 documents expanded to page level)
            Assert.Equal(109, lines.Length);

            // Each line should have 7 comma-separated fields (6 commas)
            foreach (var line in lines)
            {
                int commaCount = line.Count(c => c == ',');
                Assert.Equal(6, commaCount);
            }
        }

        [Fact]
        public async Task GenerateAsync_Opt_ContainsExpectedFields()
        {
            var request = this.CreateRequest(LoadFileFormat.Opt, 5);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            var firstLine = (await File.ReadAllLinesAsync(result.LoadFilePath))[0];
            var parts = firstLine.Split(',');

            Assert.Equal(7, parts.Length);
            Assert.StartsWith("IMG", parts[0], StringComparison.Ordinal); // Bates Number
            Assert.Equal("VOL001", parts[1]); // Volume
            Assert.Contains("IMAGES", parts[2], StringComparison.Ordinal); // ImagePath
            Assert.Equal("Y", parts[3]); // DocBreak
        }

        [Fact]
        public async Task GenerateAsync_CreatesPropertiesJson()
        {
            var request = this.CreateRequest(count: 5);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.True(File.Exists(result.PropertiesFilePath));
            var json = await File.ReadAllTextAsync(result.PropertiesFilePath);
            var doc = JsonDocument.Parse(json);

            Assert.Equal("DAT (Metadata)", doc.RootElement.GetProperty("format").GetString());
            Assert.Equal(5, doc.RootElement.GetProperty("totalRecords").GetInt64());
            Assert.False(doc.RootElement.GetProperty("chaosMode").GetProperty("enabled").GetBoolean());
        }

        [Fact]
        public async Task GenerateAsync_WithChaos_PropertiesJsonContainsAnomalies()
        {
            var request = this.CreateRequest(count: 100);
            request.Chaos = request.Chaos with { ChaosMode = true };
            request.Chaos = request.Chaos with { ChaosAmount = "5" };
            request.Metadata = request.Metadata with { Seed = 42 };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            var json = await File.ReadAllTextAsync(result.PropertiesFilePath);
            var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.GetProperty("chaosMode").GetProperty("enabled").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("chaosMode").GetProperty("totalAnomalies").GetInt32() > 0);
        }

        [Fact]
        public async Task GenerateAsync_Dat_WithLfEol_UsesCorrectLineEndings()
        {
            var request = this.CreateRequest(count: 5);
            request.Delimiters = request.Delimiters with { EndOfLine = "LF" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = Encoding.UTF8.GetString(bytes);

            // Should contain LF but not CRLF
            Assert.Contains("\n", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", content, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_WithSeed_ProducesDeterministicOutput()
        {
            var request1 = this.CreateRequest(count: 10);
            request1.Metadata = request1.Metadata with { Seed = 123 };
            var result1 = await LoadfileOnlyGenerator.GenerateAsync(request1);
            var content1 = await File.ReadAllTextAsync(result1.LoadFilePath);

            // Reset temp dir
            var tempDir2 = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir2);
            try
            {
                var request2 = this.CreateRequest(count: 10);
                request2.Metadata = request2.Metadata with { Seed = 123 };
                request2.Output = request2.Output with { OutputPath = tempDir2 };
                var result2 = await LoadfileOnlyGenerator.GenerateAsync(request2);
                var content2 = await File.ReadAllTextAsync(result2.LoadFilePath);

                Assert.Equal(content1, content2);
            }
            finally
            {
                Directory.Delete(tempDir2, true);
            }
        }

        [Fact]
        public async Task GenerateAsync_ReturnsPerformanceMetrics()
        {
            var request = this.CreateRequest(count: 10);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.Equal(10, result.TotalRecords);
            Assert.True(result.GenerationTime > TimeSpan.Zero);
            Assert.False(string.IsNullOrEmpty(result.LoadFilePath));
            Assert.False(string.IsNullOrEmpty(result.PropertiesFilePath));
        }

        [Fact]
        public async Task GenerateAsync_Dat_FileCountOne_CreatesHeaderAndOneRow()
        {
            var request = this.CreateRequest(format: LoadFileFormat.Dat, count: 1);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.True(File.Exists(result.LoadFilePath));
            var lines = await File.ReadAllLinesAsync(result.LoadFilePath);

            Assert.Equal(2, lines.Length);
            Assert.Contains("Control Number", lines[0], StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_Opt_FileCountOne_CreatesPageExpandedRows()
        {
            var request = this.CreateRequest(format: LoadFileFormat.Opt, count: 1);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.True(File.Exists(result.LoadFilePath));
            var lines = await File.ReadAllLinesAsync(result.LoadFilePath);

            Assert.Equal(2, lines.Length);

            var parts1 = lines[0].Split(',');
            Assert.Equal(7, parts1.Length);
            Assert.StartsWith("IMG", parts1[0], StringComparison.Ordinal);
            Assert.Equal("VOL001", parts1[1]);
            Assert.Equal("Y", parts1[3]); // First page is doc break

            var parts2 = lines[1].Split(',');
            Assert.Equal(7, parts2.Length);
            Assert.StartsWith("IMG", parts2[0], StringComparison.Ordinal);
            Assert.Equal("VOL001", parts2[1]);
            Assert.Equal(string.Empty, parts2[3]); // Subsequent pages are not doc break
        }

        [Fact]
        public async Task GenerateAsync_Dat_WithCrEol_UsesCorrectLineEndings()
        {
            var request = this.CreateRequest(count: 5);
            request.Delimiters = request.Delimiters with { EndOfLine = "CR" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains("\r", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", content, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_Dat_WithCrlfEol_Explicitly_UsesCrlf()
        {
            var request = this.CreateRequest(count: 5);
            request.Delimiters = request.Delimiters with { EndOfLine = "CRLF" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains("\r\n", content, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_Opt_WithLfEol_UsesCorrectLineEndings()
        {
            var request = this.CreateRequest(format: LoadFileFormat.Opt, count: 5);
            request.Delimiters = request.Delimiters with { EndOfLine = "LF" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains("\n", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", content, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_WithChaos_HundredPercent_AllLinesCorrupted()
        {
            var request = this.CreateRequest(count: 20);
            request.Chaos = request.Chaos with { ChaosMode = true };
            request.Chaos = request.Chaos with { ChaosAmount = "100%" };
            request.Metadata = request.Metadata with { Seed = 42 };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            var json = await File.ReadAllTextAsync(result.PropertiesFilePath);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            // With 100% chaos, every line should have an anomaly (header + 20 data rows = 21 lines)
            var totalAnomalies = doc.RootElement.GetProperty("chaosMode").GetProperty("totalAnomalies").GetInt32();
            Assert.Equal(1 + request.Output.FileCount, totalAnomalies);
        }

        [Fact]
        public async Task GenerateAsync_WithNullEol_FallsBackToCrlf()
        {
            var request = this.CreateRequest(count: 3);
            request.Delimiters = request.Delimiters with { EndOfLine = string.Empty };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains("\r\n", content, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenerateAsync_PropertiesJson_ContainsFormatAndRecordCount()
        {
            var request = this.CreateRequest(format: LoadFileFormat.Opt, count: 25);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            var json = await File.ReadAllTextAsync(result.PropertiesFilePath);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal("OPT (Image)", doc.RootElement.GetProperty("format").GetString());
            Assert.Equal(25, doc.RootElement.GetProperty("totalRecords").GetInt64());
        }

        [Fact]
        public async Task GenerateAsync_WithSeed_LoadfileOnly_DatAndOpt_BothDeterministic()
        {
            var request1 = this.CreateRequest(format: LoadFileFormat.Dat, count: 5);
            request1.Metadata = request1.Metadata with { Seed = 42 };
            var result1 = await LoadfileOnlyGenerator.GenerateAsync(request1);
            var content1 = await File.ReadAllTextAsync(result1.LoadFilePath);

            var request2 = this.CreateRequest(format: LoadFileFormat.Opt, count: 5);
            request2.Metadata = request2.Metadata with { Seed = 42 };
            var result2 = await LoadfileOnlyGenerator.GenerateAsync(request2);
            var content2 = await File.ReadAllTextAsync(result2.LoadFilePath);

            // DAT should have header, OPT should not
            Assert.Contains("Control Number", content1, StringComparison.Ordinal);
            Assert.DoesNotContain("Control Number", content2, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies that when encoding chaos is enabled in DAT loadfile-only mode, the encoding anomaly
        /// is successfully written after the header line and the last data line when targeted.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [Fact]
        public async Task GenerateAsync_LoadfileOnly_Dat_EncodingChaos_HeaderAndLastLine_Injected()
        {
            var request = this.CreateRequest(format: LoadFileFormat.Dat, count: 5);
            request.Chaos = request.Chaos with
            {
                ChaosMode = true,
                ChaosAmount = "100%",
                ChaosTypes = "encoding",
            };
            request.Metadata = request.Metadata with { Seed = 42 };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            // Verify properties JSON audit has all expected anomalies
            var json = await File.ReadAllTextAsync(result.PropertiesFilePath);
            var doc = JsonDocument.Parse(json);
            var totalAnomalies = doc.RootElement.GetProperty("chaosMode").GetProperty("totalAnomalies").GetInt32();

            // Total lines = 1 header + 5 data lines = 6 lines.
            // Under 100% chaos, all 6 lines should have an anomaly.
            Assert.Equal(6, totalAnomalies);

            // Let's also read the load file bytes and verify the invalid bytes (0xFE, 0xFF for UTF-8) are present.
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);

            // The encoding anomaly bytes for UTF-8 are 0xFE, 0xFF.
            // Let's verify how many times they appear in the file.
            int occurrences = 0;
            for (int i = 0; i < bytes.Length - 1; i++)
            {
                if (bytes[i] == 0xFE && bytes[i + 1] == 0xFF)
                {
                    occurrences++;
                }
            }

            // 6 lines targeted -> 6 boundaries where we expect the encoding anomalies
            Assert.Equal(6, occurrences);
        }

        [Fact]
        public async Task GenerateAsync_Dat_LoadfileOnly_WritesInStreamingFashion()
        {
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat, WriterMode.LoadfileOnly);

            // Warm up to force JIT compilation of all writer code
            var warmupRequest = this.CreateRequest(format: LoadFileFormat.Dat, count: 5);
            using var warmupMs = new MemoryStream();
            await writer.WriteAsync(warmupMs, warmupRequest, new List<FileData>(), null);

            var request = this.CreateRequest(format: LoadFileFormat.Dat, count: 50000);
            using var ms = new MemoryStream();
            using var assertionStream = new StreamingAssertionStream(ms, maxWriteSize: 16384);

            await writer.WriteAsync(assertionStream, request, new List<FileData>(), null);
            await assertionStream.FlushAsync();

            Assert.True(assertionStream.WriteSizes.Count > 0, "No bytes were written to the stream.");
            Assert.All(assertionStream.WriteSizes, size => Assert.True(size <= 16384, $"Write size {size} exceeds 16KB buffer limit."));
            Assert.True(assertionStream.PeakMemoryUsage < 35 * 1024 * 1024, $"Peak memory usage {assertionStream.PeakMemoryUsage} bytes exceeded the 35MB streaming limit.");
        }

        [Fact]
        public async Task GenerateAsync_Opt_LoadfileOnly_WritesInStreamingFashion()
        {
            var writer = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt, WriterMode.LoadfileOnly);

            // Warm up to force JIT compilation of all writer code
            var warmupRequest = this.CreateRequest(format: LoadFileFormat.Opt, count: 5);
            using var warmupMs = new MemoryStream();
            await writer.WriteAsync(warmupMs, warmupRequest, new List<FileData>(), null);

            var request = this.CreateRequest(format: LoadFileFormat.Opt, count: 50000);
            using var ms = new MemoryStream();
            using var assertionStream = new StreamingAssertionStream(ms, maxWriteSize: 16384);

            await writer.WriteAsync(assertionStream, request, new List<FileData>(), null);
            await assertionStream.FlushAsync();

            Assert.True(assertionStream.WriteSizes.Count > 0, "No bytes were written to the stream.");
            Assert.All(assertionStream.WriteSizes, size => Assert.True(size <= 16384, $"Write size {size} exceeds 16KB buffer limit."));
            Assert.True(assertionStream.PeakMemoryUsage < 35 * 1024 * 1024, $"Peak memory usage {assertionStream.PeakMemoryUsage} bytes exceeded the 35MB streaming limit.");
        }

        [Fact]
        public async Task GenerateAsync_TiffAndJpgWithoutExplicitFormat_CreatesBothDatAndOpt()
        {
            // TIFF
            var tiffRequest = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = this.tempDir,
                    FileCount = 5,
                    FileType = "tiff",
                },
                LoadFile = new LoadFileConfig
                {
                    LoadFileFormat = LoadFileFormat.Dat,
                    LoadFileFormats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt },
                    Encoding = "UTF-8",
                },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
                Metadata = new MetadataConfig { Seed = 42 },
                LoadfileOnly = true,
            };

            var tiffResult = await LoadfileOnlyGenerator.GenerateAsync(tiffRequest);

            var datFiles = Directory.GetFiles(this.tempDir, "*.dat");
            var optFiles = Directory.GetFiles(this.tempDir, "*.opt");

            Assert.Single(datFiles);
            Assert.Single(optFiles);

            // Delete all files in tempDir
            foreach (var f in Directory.GetFiles(this.tempDir))
            {
                File.Delete(f);
            }

            // JPG
            var jpgRequest = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = this.tempDir,
                    FileCount = 5,
                    FileType = "jpg",
                },
                LoadFile = new LoadFileConfig
                {
                    LoadFileFormat = LoadFileFormat.Dat,
                    LoadFileFormats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt },
                    Encoding = "UTF-8",
                },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
                Metadata = new MetadataConfig { Seed = 42 },
                LoadfileOnly = true,
            };

            var jpgResult = await LoadfileOnlyGenerator.GenerateAsync(jpgRequest);

            datFiles = Directory.GetFiles(this.tempDir, "*.dat");
            optFiles = Directory.GetFiles(this.tempDir, "*.opt");

            Assert.Single(datFiles);
            Assert.Single(optFiles);
        }

        [Fact]
        public async Task GenerateAsync_TiffWithBates_ProducesCorrectPageSuffixes()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = this.tempDir,
                    FileCount = 2,
                    FileType = "tiff",
                },
                LoadFile = new LoadFileConfig
                {
                    LoadFileFormat = LoadFileFormat.Opt,
                    Encoding = "UTF-8",
                },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
                Metadata = new MetadataConfig { Seed = 42 },
                Bates = new BatesNumberConfig
                {
                    Prefix = "ABC001_",
                    Start = 1,
                    Digits = 5,
                },
                LoadfileOnly = true,
            };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.True(File.Exists(result.LoadFilePath));
            var lines = await File.ReadAllLinesAsync(result.LoadFilePath);

            // Verify first page of first document
            var parts1 = lines[0].Split(',');
            Assert.StartsWith("ABC001_00001", parts1[0], StringComparison.Ordinal);
            if (parts1[0].Contains("_", StringComparison.Ordinal))
            {
                Assert.Equal("ABC001_00001_001", parts1[0]);
            }

            Assert.Equal("Y", parts1[3]); // doc break

            // If there's a second page for first document, verify no doc break
            if (lines.Length > 1 && lines[1].StartsWith("ABC001_00001", StringComparison.Ordinal))
            {
                var parts2 = lines[1].Split(',');
                Assert.Equal("ABC001_00001_002", parts2[0]);
                Assert.Equal(string.Empty, parts2[3]); // no doc break
            }
        }

        private class StreamingAssertionStream : Stream
        {
            private readonly Stream inner;
            private readonly int maxWriteSize;
            private readonly long startMemory;

            public StreamingAssertionStream(Stream inner, int maxWriteSize)
            {
                this.inner = inner;
                this.maxWriteSize = maxWriteSize;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                this.startMemory = GC.GetTotalMemory(true);
            }

            public List<int> WriteSizes { get; } = new List<int>();

            public long PeakMemoryUsage { get; private set; }

            public override bool CanRead => this.inner.CanRead;

            public override bool CanSeek => this.inner.CanSeek;

            public override bool CanWrite => this.inner.CanWrite;

            public override long Length => this.inner.Length;

            public override long Position { get => this.inner.Position; set => this.inner.Position = value; }

            public override void Flush() => this.inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => this.inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => this.inner.Seek(offset, origin);

            public override void SetLength(long value) => this.inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                this.WriteSizes.Add(count);

                if (count > this.maxWriteSize)
                {
                    throw new InvalidOperationException($"Write size of {count} bytes exceeds the maximum streaming write size of {this.maxWriteSize} bytes.");
                }

                this.TrackMemory();
                this.inner.Write(buffer, offset, count);
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                this.WriteSizes.Add(count);

                if (count > this.maxWriteSize)
                {
                    throw new InvalidOperationException($"Write size of {count} bytes exceeds the maximum streaming write size of {this.maxWriteSize} bytes.");
                }

                this.TrackMemory();
                await inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                this.WriteSizes.Add(buffer.Length);

                if (buffer.Length > this.maxWriteSize)
                {
                    throw new InvalidOperationException($"Write size of {buffer.Length} bytes exceeds the maximum streaming write size of {this.maxWriteSize} bytes.");
                }

                this.TrackMemory();
                await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            private void TrackMemory()
            {
                if (this.WriteSizes.Count % 500 == 0)
                {
                    GC.Collect();
                    long currentMemory = GC.GetTotalMemory(true) - this.startMemory;
                    if (currentMemory > this.PeakMemoryUsage)
                    {
                        this.PeakMemoryUsage = currentMemory;
                    }
                }
            }
        }
    }
}
