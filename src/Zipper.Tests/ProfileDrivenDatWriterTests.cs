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
            var lines = content.Split('
', StringSplitOptions.RemoveEmptyEntries);

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
            var lines = content.Split('
', StringSplitOptions.RemoveEmptyEntries);

            // Each data field should be wrapped in the quote delimiter (þ)
            Assert.Contains("þ", lines[1]);
        }

        [Fact]
        public async Task WriteAsync_FieldContainingQuoteDelimiter_IsDoubled()
        {
            // Build a coded column whose only value contains the DAT quote delimiter (þ)
            var profile = new ColumnProfile
            {
                Name = "escape-test",
                Settings = new ProfileSettings { EmptyValuePercentage = 0 },
                DataSources = new Dictionary<string, DataSourceConfig>
                {
                    ["quoteValues"] = new DataSourceConfig
                    {
                        Values = new List<string> { "valþSpecial" },
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

            // þ inside the field value must be doubled to þþ (Concordance escape rule)
            Assert.Contains("valþþSpecial", content);
        }

        [Fact]
        public async Task WriteAsync_FieldContainingNewline_IsSanitized()
        {
            // Coded column whose only value contains a newline character
            var profile = new ColumnProfile
            {
                Name = "newline-test",
                Settings = new ProfileSettings { EmptyValuePercentage = 0 },
                DataSources = new Dictionary<string, DataSourceConfig>
                {
                    ["nlValues"] = new DataSourceConfig
                    {
                        Values = new List<string> { "line1
line2" },
                    },
                },
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "DOCID", Type = "identifier", Required = true },
                    new() { Name = "NLFIELD", Type = "coded", DataSource = "nlValues", EmptyPercentage = 0 },
                },
            };

            var request = MakeRequest(profile, 1);

            // Use a visible newline-replacement token so we can assert it appears
            request.Delimiters = request.Delimiters with { NewlineDelimiter = "<NL>" };
            var content = await CaptureOutputAsync(request);

            // The raw newline should be replaced by the configured newline delimiter
            var dataLine = content.Split(new[] { "
", "
" }, StringSplitOptions.RemoveEmptyEntries)[1];
            Assert.DoesNotContain('
', dataLine);
            Assert.Contains("<NL>", content);
        }
    }
}
