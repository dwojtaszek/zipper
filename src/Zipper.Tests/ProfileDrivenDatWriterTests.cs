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

        private static FileGenerationRequest MakeRequest(ColumnProfile profile, int fileCount = 3)
        {
            return new FileGenerationRequest
            {
                Output = new OutputConfig { FileCount = fileCount, FileType = "pdf" },
                Metadata = new MetadataConfig { ColumnProfile = profile, Seed = 42 },
                LoadFile = new LoadFileConfig { Encoding = "UTF-8" },
                Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            };
        }

        private static async Task<string> CaptureOutputAsync(FileGenerationRequest request)
        {
            var writer = new ProfileDrivenDatWriter();
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
    }
}
