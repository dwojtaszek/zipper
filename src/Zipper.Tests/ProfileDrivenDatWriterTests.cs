using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;

namespace Zipper
{
    public class ProfileDrivenDatWriterTests
    {
        private static ColumnProfile MakeMinimalProfile()
        {
            return new ColumnProfile
            {
                Name = "minimal",
                Settings = new ProfileSettings { EmptyValuePercentage = 0 },
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "DOCID", Type = "identifier", Required = true },
                    new() { Name = "FILEPATH", Type = "text", Required = true },
                },
            };
        }

        private static FileGenerationRequest MakeRequest(ColumnProfile profile, int fileCount = 3, int? custodianCountOverride = null)
        {
            return new FileGenerationRequest
            {
                Output = new OutputConfig { FileCount = fileCount, FileType = "pdf" },
                Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42, CustodianCountOverride = custodianCountOverride },
                LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            };
        }

        private static async Task<string> CaptureOutputAsync(FileGenerationRequest request)
        {
            var writer = new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly);
            using var stream = new MemoryStream();
            await writer.WriteAsync(stream, request, new List<FileData>());
            stream.Position = 0;
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        [Fact]
        public async Task WriteAsync_ProducesHeaderFromProfileColumns()
        {
            var profile = MakeMinimalProfile();
            var request = MakeRequest(profile);
            var content = await CaptureOutputAsync(request);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Header + 3 data rows
            Assert.Equal(4, lines.Length);
            Assert.Contains("DOCID", lines[0]);
            Assert.Contains("FILEPATH", lines[0]);
        }

        [Fact]
        public async Task WriteAsync_DataRows_WrappedInQuoteDelimiter()
        {
            var profile = MakeMinimalProfile();
            var request = MakeRequest(profile, 1);
            var content = await CaptureOutputAsync(request);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Each data field should be wrapped in the quote delimiter (\u00fe)
            Assert.Contains("\u00fe", lines[1]);
        }

        [Fact]
        public async Task WriteAsync_FieldContainingQuoteDelimiter_IsDoubled()
        {
            var profile = new ColumnProfile
            {
                Name = "escape-test",
                Settings = new ProfileSettings { EmptyValuePercentage = 0 },
                DataSources = new Dictionary<string, DataSourceConfig>
                {
                    ["quoteValues"] = new DataSourceConfig
                    {
                        Values = new List<string> { "val\u00feSpecial" },
                    },
                },
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "DOCID", Type = "identifier", Required = true },
                    new() { Name = "QUOTEFIELD", Type = "coded", DataSource = "quoteValues", EmptyPercentage = 0 },
                },
            };

            var request = MakeRequest(profile, 1);
            var content = await CaptureOutputAsync(request);

            // \u00fe inside the field value must be doubled to \u00fe\u00fe per Concordance escaping
            Assert.Contains("val\u00fe\u00feSpecial", content);
        }

        /// <summary>
        /// When CustodianCountOverride is set on the request, ProfileDrivenDatWriter
        /// must pass it to DataGenerator so custodian values are bounded to that count.
        /// </summary>
        [Fact]
        public async Task WriteAsync_WithCustodianCountOverride_LimitsCustodianValuesToOverrideCount()
        {
            // Standard profile has 25 custodians; we override to 2
            var profile = BuiltInProfiles.Standard;
            var request = MakeRequest(profile, fileCount: 200, custodianCountOverride: 2);
            var content = await CaptureOutputAsync(request);

            // Parse CUSTODIAN column values from all data rows
            var lines = content.Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries);
            var headerLine = lines[0];
            var colDelim = '\u0014';
            var quote = '\u00fe';
            var headers = headerLine.Split(colDelim)
                .Select(h => h.Trim(quote))
                .ToList();
            var custodianIdx = headers.IndexOf("CUSTODIAN");
            Assert.True(custodianIdx >= 0, "CUSTODIAN column not found in profile output");

            var custodianValues = new HashSet<string>();
            foreach (var line in lines.Skip(1))
            {
                var fields = line.Split(colDelim);
                if (custodianIdx < fields.Length)
                {
                    var val = fields[custodianIdx].Trim(quote);
                    if (!string.IsNullOrEmpty(val))
                    {
                        custodianValues.Add(val);
                    }
                }
            }

            // With override=2, must only see Custodian_1 and/or Custodian_2
            Assert.True(
                custodianValues.Count <= 2,
                $"Expected at most 2 distinct custodians but found {custodianValues.Count}: {string.Join(", ", custodianValues)}");
            foreach (var v in custodianValues)
            {
                Assert.Matches(@"^Custodian_[12]$", v);
            }
        }

        /// <summary>
        /// Tests that a chaos encoding anomaly successfully injects invalid bytes on both the header boundary
        /// and the final data record boundary.
        /// </summary>
        [Fact]
        public async Task WriteAsync_WithChaosEncoding_TargetsHeaderAndLastLine_InjectsInvalidBytesAndCreatesAudit()
        {
            var profile = MakeMinimalProfile();
            var request = MakeRequest(profile);
            request.Output = request.Output with { FileCount = 3 };

            using var baseStream = new MemoryStream();
            await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(baseStream, request, []);
            var baseBytes = baseStream.ToArray();

            var chaosEngine = new ChaosEngine(
                totalLines: 4,
                chaosAmount: "100%",
                chaosTypes: "encoding",
                format: LoadFileFormat.Dat,
                columnDelimiter: "\u0014",
                quoteDelimiter: "\u00fe",
                eol: "\r\n",
                seed: 42);

            using var chaosStream = new MemoryStream();
            await new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly).WriteAsync(chaosStream, request, [], chaosEngine);
            var chaosBytes = chaosStream.ToArray();

            var headerAnomaly = chaosEngine.Anomalies.FirstOrDefault(a => a.LineNumber == "Boundary 1-2");
            var lastLineAnomaly = chaosEngine.Anomalies.FirstOrDefault(a => a.LineNumber == "Boundary 4-5");

            Assert.NotNull(headerAnomaly);
            Assert.NotNull(lastLineAnomaly);
            Assert.True(chaosBytes.Length > baseBytes.Length, "Encoding chaos should inject extra bytes");
        }
    }
}
