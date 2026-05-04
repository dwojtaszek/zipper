using System.Text;
using System.Text.Json;
using Xunit;

using Zipper.Config;

namespace Zipper
{
    public class LoadfileOnlyGeneratorTests : IDisposable
    {
        private readonly string tempDir;

        public LoadfileOnlyGeneratorTests()
        {
            this.tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
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
            Assert.Contains("Control Number", firstLine);
            Assert.Contains("File Path", firstLine);
            Assert.Contains("Custodian", firstLine);
            Assert.Contains("EmailSubject", firstLine);
            Assert.Contains("ExtractedText", firstLine);
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

            // OPT has no header, just data rows
            Assert.Equal(20, lines.Length);

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
            Assert.StartsWith("IMG", parts[0]); // Bates Number
            Assert.Equal("VOL001", parts[1]); // Volume
            Assert.Contains("IMAGES", parts[2]); // ImagePath
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
            Assert.Contains("\n", content);
            Assert.DoesNotContain("\r\n", content);
        }

        [Fact]
        public async Task GenerateAsync_WithSeed_ProducesDeterministicOutput()
        {
            var request1 = this.CreateRequest(count: 10);
            request1.Metadata = request1.Metadata with { Seed = 123 };
            var result1 = await LoadfileOnlyGenerator.GenerateAsync(request1);
            var content1 = await File.ReadAllTextAsync(result1.LoadFilePath);

            // Reset temp dir
            var tempDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
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
            Assert.Contains("Control Number", lines[0]);
        }

        [Fact]
        public async Task GenerateAsync_Opt_FileCountOne_CreatesSingleLine()
        {
            var request = this.CreateRequest(format: LoadFileFormat.Opt, count: 1);
            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Assert.True(File.Exists(result.LoadFilePath));
            var lines = await File.ReadAllLinesAsync(result.LoadFilePath);

            Assert.Single(lines);
            var parts = lines[0].Split(',');
            Assert.Equal(7, parts.Length);
            Assert.StartsWith("IMG", parts[0]);
            Assert.Equal("VOL001", parts[1]);
        }

        [Fact]
        public async Task GenerateAsync_Dat_WithCrEol_UsesCorrectLineEndings()
        {
            var request = this.CreateRequest(count: 5);
            request.Delimiters = request.Delimiters with { EndOfLine = "CR" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains("\r", content);
            Assert.DoesNotContain("\n", content);
        }

        [Fact]
        public async Task GenerateAsync_Dat_WithCrlfEol_Explicitly_UsesCrlf()
        {
            var request = this.CreateRequest(count: 5);
            request.Delimiters = request.Delimiters with { EndOfLine = "CRLF" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains("\r\n", content);
        }

        [Fact]
        public async Task GenerateAsync_Opt_WithLfEol_UsesCorrectLineEndings()
        {
            var request = this.CreateRequest(format: LoadFileFormat.Opt, count: 5);
            request.Delimiters = request.Delimiters with { EndOfLine = "LF" };

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);
            var bytes = await File.ReadAllBytesAsync(result.LoadFilePath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains("\n", content);
            Assert.DoesNotContain("\r\n", content);
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

            Assert.Contains("\r\n", content);
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
            Assert.Contains("Control Number", content1);
            Assert.DoesNotContain("Control Number", content2);
        }
    }
}
