using Xunit;
using Zipper.LoadFiles;

namespace Zipper.Tests;

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
            var outputPath = Path.Combine(this.TempDir, $"test.{format.ToString().ToLowerInvariant()}");

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
            var outputPath = Path.Combine(this.TempDir, $"test.{format.ToString().ToLowerInvariant()}");

            await using (var stream = File.OpenWrite(outputPath))
            {
                await writer.WriteAsync(stream, request, fileData);
            }

            var content = await File.ReadAllTextAsync(outputPath);
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (format == LoadFileFormat.EdrmXml)
            {
                var doc1Index = content.IndexOf("DocID=\"DOC00000001\"");
                var doc2Index = content.IndexOf("DocID=\"DOC00000002\"");
                var doc3Index = content.IndexOf("DocID=\"DOC00000003\"");

                Assert.True(doc1Index != -1);
                Assert.True(doc2Index != -1);
                Assert.True(doc3Index != -1);

                var doc1Content = content.Substring(doc1Index, doc2Index - doc1Index);
                var doc2Content = content.Substring(doc2Index, doc3Index - doc2Index);
                var doc3Content = content.Substring(doc3Index);

                Assert.Contains("Name=\"PageCount\"", doc1Content);
                Assert.Contains(">5<", doc1Content);

                Assert.Contains("Name=\"PageCount\"", doc2Content);
                Assert.Contains(">7<", doc2Content);

                Assert.Contains("Name=\"PageCount\"", doc3Content);
                Assert.Contains(">3<", doc3Content);
            }
            else if (format == LoadFileFormat.Opt)
            {
                var line1 = lines.FirstOrDefault(l => l.StartsWith("DOC00000001_001"));
                var line2 = lines.FirstOrDefault(l => l.StartsWith("DOC00000002_001"));
                var line3 = lines.FirstOrDefault(l => l.StartsWith("DOC00000003_001"));

                Assert.NotNull(line1);
                Assert.NotNull(line2);
                Assert.NotNull(line3);

                Assert.EndsWith(",5", line1);
                Assert.EndsWith(",7", line2);
                Assert.EndsWith(",3", line3);
            }
            else
            {
                var line1 = lines.FirstOrDefault(l => l.Contains("DOC00000001"));
                var line2 = lines.FirstOrDefault(l => l.Contains("DOC00000002"));
                var line3 = lines.FirstOrDefault(l => l.Contains("DOC00000003"));

                Assert.NotNull(line1);
                Assert.NotNull(line2);
                Assert.NotNull(line3);

                var delimiter = format == LoadFileFormat.Csv ? ',' : (char)20;
                if (format == LoadFileFormat.Concordance)
                {
                    delimiter = (char)20;
                }

                var headerFields = lines[0].Split(delimiter).Select(f => f.Trim('\"', (char)254)).ToList();
                var pageCountColIdx = headerFields.FindIndex(f => string.Equals(f.Replace(" ", string.Empty), "PAGECOUNT", StringComparison.OrdinalIgnoreCase));
                Assert.True(pageCountColIdx != -1, $"PAGECOUNT column not found in header for format {format}");

                var fields1 = line1.Split(delimiter).Select(f => f.Trim('\"', (char)254)).ToList();
                var fields2 = line2.Split(delimiter).Select(f => f.Trim('\"', (char)254)).ToList();
                var fields3 = line3.Split(delimiter).Select(f => f.Trim('\"', (char)254)).ToList();

                Assert.Equal("5", fields1[pageCountColIdx]);
                Assert.Equal("7", fields2[pageCountColIdx]);
                Assert.Equal("3", fields3[pageCountColIdx]);
            }
        }
    }
}
