using System.Text;
using Xunit;
using Zipper.Cli;
using Zipper.Config;
using Zipper.LoadFiles;
using Zipper.Profiles;
using Zipper.Utils;

namespace Zipper.Tests
{
    public class FieldNamingTests : IDisposable
    {
        private readonly string tempDir;

        public FieldNamingTests()
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

        private FileGenerationRequest CreateRequest(string namingConvention)
        {
            var profile = new ColumnProfile
            {
                Name = "test",
                FieldNamingConvention = namingConvention,
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "DocID", Type = "identifier", Required = true },
                    new() { Name = "CustodianName", Type = "text" }
                }
            };

            return new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = this.tempDir,
                    FileCount = 1,
                    FileType = "pdf"
                },
                LoadFile = new LoadFileConfig
                {
                    LoadFileFormat = LoadFileFormat.Dat,
                    Encoding = "UTF-8"
                },
                Metadata = new MetadataConfig
                {
                    WithMetadata = true,
                    ColumnProfile = profile
                },
                Delimiters = new DelimiterConfig
                {
                    ColumnDelimiter = "|",
                    QuoteDelimiter = "^",
                    EndOfLine = "LF"
                }
            };
        }

        [Theory]
        [InlineData("UPPERCASE", "DOCID|CUSTODIANNAME")]
        [InlineData("lowercase", "docid|custodianname")]
        [InlineData("PascalCase", "DocId|CustodianName")]
        [InlineData("snake_case", "doc_id|custodian_name")]
        public async Task ProfileDrivenDatWriter_ShouldRespectFieldNamingConvention(string convention, string expectedHeader)
        {
            ArgumentNullException.ThrowIfNull(expectedHeader);

            // Arrange
            var request = this.CreateRequest(convention);
            var writer = new ProfileDrivenDatWriter();
            var stream = new MemoryStream();

            // Act
            await writer.WriteAsync(stream, request, new List<FileData>());

            // Assert
            var content = Encoding.UTF8.GetString(stream.ToArray());
            var header = content.Split('\n')[0].Trim('^'); // Simple split, ignoring complex escaping for now

            // The actual header will have quotes if configured. In CreateRequest I set ^ as quote.
            // Expected header for UPPERCASE with ^ quote would be ^DOCID^|^CUSTODIANNAME^
            Assert.Contains(expectedHeader.Replace("|", "^|^"), content);
        }

        [Theory]
        [InlineData("UPPERCASE", "CUSTODIAN")]
        [InlineData("lowercase", "custodian")]
        public async Task DatWriter_ShouldRespectFieldNamingConvention(string convention, string expectedField)
        {
            ArgumentNullException.ThrowIfNull(expectedField);

            // Arrange
            var request = this.CreateRequest(convention);
            var writer = new DatWriter();
            var fileData = new List<FileData>
            {
                new FileData
                {
                    WorkItem = new FileWorkItem { Index = 1, FolderNumber = 1, FilePathInZip = "test.pdf" }
                }
            };
            var stream = new MemoryStream();

            // Act
            await writer.WriteAsync(stream, request, fileData);

            // Assert
            var content = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains(expectedField, content);
        }

        [Fact]
        public void NamingConventionHelper_ShouldCollapseConsecutiveSeparators()
        {
            var input = "Control - - Number";
            var result = NamingConventionHelper.ApplyConvention(input, "snake_case");
            Assert.Equal("control_number", result);
        }

        [Fact]
        public void ColumnProfileLoader_ShouldThrowOnInvalidConvention()
        {
            var profile = new ColumnProfile
            {
                Name = "invalid-test",
                FieldNamingConvention = "INVALID_CONVENTION",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "DocID", Type = "identifier" }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => ColumnProfileLoader.Validate(profile));
            Assert.Contains("has an invalid fieldNamingConvention", ex.Message);
        }

        [Fact]
        public void CliValidator_ShouldRejectInvalidFormatInLoadFileOnlyMode()
        {
            var parsed = new ParsedArguments
            {
                LoadfileOnly = true,
                LoadFileFormat = "csv",
                Count = 100,
                OutputDirectory = new DirectoryInfo(this.tempDir),
                FileType = "pdf"
            };

            var isValid = CliValidator.Validate(parsed);
            Assert.False(isValid);
        }
    }
}
