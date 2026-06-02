using Xunit;
using Zipper.LoadFiles;

namespace Zipper.Tests
{
    public class AllWritersTests : TempDirectoryTestBase
    {
        [Fact]
        public void WriterFactory_WithAllFormats_ShouldReturnCorrectWriters()
        {
            var datWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat);
            Assert.IsType<DatWriter>(datWriter);
            Assert.Equal("DAT", datWriter.FormatName);
            Assert.Equal(".dat", datWriter.FileExtension);

            var optWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt);
            Assert.Equal("OPT", optWriter.FormatName);
            Assert.Equal(".opt", optWriter.FileExtension);

            var csvWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Csv);
            Assert.Equal("CSV", csvWriter.FormatName);
            Assert.Equal(".csv", csvWriter.FileExtension);

            var xmlWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.EdrmXml);
            Assert.IsType<XmlLoadFileWriter>(xmlWriter);
            Assert.Equal("XML", xmlWriter.FormatName);
            Assert.Equal(".xml", xmlWriter.FileExtension);

            var concordanceWriter = LoadFileWriterFactory.CreateWriter(LoadFileFormat.Concordance);
            Assert.Equal("CONCORDANCE", concordanceWriter.FormatName);
        }

        [Fact]
        public async Task AllWriters_WithBatesConfig_ShouldIncludeBatesNumber()
        {
            var request = this.CreateTestRequest();
            request.Bates = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 6,
                Increment = 1,
            };
            var fileData = this.CreateTestFileData();

            foreach (LoadFileFormat format in Enum.GetValues(typeof(LoadFileFormat)))
            {
                var writer = LoadFileWriterFactory.CreateWriter(format);
                var outputPath = Path.Combine(this.TempDir, $"test.{format.ToString().ToLower()}");

                await using (var stream = File.OpenWrite(outputPath))
                {
                    await writer.WriteAsync(stream, request, fileData);
                }

                var content = await File.ReadAllTextAsync(outputPath);

                Assert.Contains("TEST", content);
                Assert.Contains("000001", content);
            }
        }

        [Fact]
        public async Task AllWriters_WithTiffPageRange_ShouldIncludePageCount()
        {
            var request = this.CreateTestRequest("tiff");
            request.Tiff = request.Tiff with { PageRange = (1, 10) };
            var fileData = this.CreateTestFileData();
            fileData[0] = fileData[0] with { PageCount = 5 };
            fileData[1] = fileData[1] with { PageCount = 7 };
            fileData[2] = fileData[2] with { PageCount = 3 };

            foreach (LoadFileFormat format in Enum.GetValues(typeof(LoadFileFormat)))
            {
                var writer = LoadFileWriterFactory.CreateWriter(format);
                var outputPath = Path.Combine(this.TempDir, $"test.{format.ToString().ToLower()}");

                await using (var stream = File.OpenWrite(outputPath))
                {
                    await writer.WriteAsync(stream, request, fileData);
                }

                var content = await File.ReadAllTextAsync(outputPath);

                Assert.Contains("5", content);
                Assert.Contains("7", content);
                Assert.Contains("3", content);
            }
        }
    }
}
