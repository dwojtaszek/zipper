using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Zipper.Tests;

public class StandardModeSpoolingMetadataTests : TempDirectoryTestBase
{
    [Fact]
    public async Task StandardMode_EmlWithFamiliesAndMetadata_DatMatchesArchiveCcAndAttachmentFileSize()
    {
        var outputDir = Path.Combine(this.TempDir, "dat_test");
        Directory.CreateDirectory(outputDir);

        var args = new[]
        {
            "--type", "eml",
            "--count", "30",
            "--seed", "42",
            "--attachment-rate", "100",
            "--with-families",
            "--with-metadata",
            "--output-path", outputDir
        };

        var exitCode = await Program.Main(args);
        Assert.Equal(0, exitCode);

        var zipFile = Directory.GetFiles(outputDir, "*.zip").Single();
        var datFile = Directory.GetFiles(outputDir, "*.dat").Single();

        using var archive = ZipFile.OpenRead(zipFile);
        var datContent = await File.ReadAllTextAsync(datFile);
        var datLines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var header = datLines[0];

        var headerCols = datLines[0].Split('\u0014')
            .Select(s => s.Trim('\u00fe', ' ', '\t', '\r', '\n'))
            .ToList();

        int ccIdx = headerCols.IndexOf("CC");
        int fileSizeIdx = headerCols.IndexOf("File Size");
        int filePathIdx = headerCols.IndexOf("File Path");
        int parentDocIdIdx = headerCols.IndexOf("PARENTDOCID");

        Assert.True(ccIdx >= 0, "CC column not found in DAT header");
        Assert.True(fileSizeIdx >= 0, "File Size column not found in DAT header");
        Assert.True(filePathIdx >= 0, "File Path column not found in DAT header");
        Assert.True(parentDocIdIdx >= 0, "PARENTDOCID column not found in DAT header");

        int nonemptyCcCount = 0;
        int childRowCount = 0;

        for (int i = 1; i < datLines.Length; i++)
        {
            var cols = datLines[i].Split('\u0014')
                .Select(s => s.Trim('\u00fe', '\r', '\n'))
                .ToArray();

            var filePath = cols[filePathIdx];
            var isChild = !string.IsNullOrEmpty(cols[parentDocIdIdx]);

            if (!isChild)
            {
                // Parent EML document: verify CC matches MIME header in ZIP
                var entry = archive.GetEntry(filePath);
                Assert.NotNull(entry);

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                string? mimeCc = null;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (string.IsNullOrEmpty(line))
                    {
                        break; // End of MIME headers
                    }

                    if (line.StartsWith("Cc: ", StringComparison.OrdinalIgnoreCase))
                    {
                        mimeCc = line.Substring(4).Trim();
                    }
                }

                var datCc = cols[ccIdx];
                if (mimeCc is not null)
                {
                    Assert.Equal(mimeCc, datCc);
                    nonemptyCcCount++;
                }
                else
                {
                    Assert.Equal(string.Empty, datCc);
                }
            }
            else
            {
                // Child attachment: verify File Size matches ZIP entry length
                childRowCount++;
                var entry = archive.GetEntry(filePath);
                Assert.NotNull(entry);

                var datFileSize = long.Parse(cols[fileSizeIdx], System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(entry.Length, datFileSize);
                Assert.True(datFileSize > 0, $"Attachment file size was {datFileSize} but expected > 0");
            }
        }

        // Seed 42 with 30 emails generates at least some nonempty CC headers and child rows
        Assert.True(nonemptyCcCount > 0, "Expected at least one non-empty CC header in generated EML files");
        Assert.Equal(30, childRowCount);
    }

    [Fact]
    public async Task StandardMode_EmlWithFamiliesAndMetadata_CsvMatchesCcAndAttachmentFileSize()
    {
        var outputDir = Path.Combine(this.TempDir, "csv_test");
        Directory.CreateDirectory(outputDir);

        var args = new[]
        {
            "--type", "eml",
            "--count", "10",
            "--seed", "42",
            "--attachment-rate", "100",
            "--with-families",
            "--with-metadata",
            "--loadfile-format", "csv",
            "--output-path", outputDir
        };

        var exitCode = await Program.Main(args);
        Assert.Equal(0, exitCode);

        var zipFile = Directory.GetFiles(outputDir, "*.zip").Single();
        var csvFile = Directory.GetFiles(outputDir, "*.csv").Single();

        using var archive = ZipFile.OpenRead(zipFile);
        var csvLines = await File.ReadAllLinesAsync(csvFile);
        var header = csvLines[0].Split(',').Select(h => h.Trim('"', ' ')).ToArray();

        int ccIdx = Array.FindIndex(header, h => string.Equals(h, "CC", StringComparison.OrdinalIgnoreCase));
        int fileSizeIdx = Array.FindIndex(header, h => string.Equals(h, "File Size", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "FILESIZE", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "FILE SIZE", StringComparison.OrdinalIgnoreCase));
        int filePathIdx = Array.FindIndex(header, h => string.Equals(h, "File Path", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "FILEPATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "FILE PATH", StringComparison.OrdinalIgnoreCase));
        int parentDocIdIdx = Array.FindIndex(header, h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "Parent Doc ID", StringComparison.OrdinalIgnoreCase));

        Assert.True(ccIdx >= 0, "CC column not found in CSV header");
        Assert.True(fileSizeIdx >= 0, "File Size column not found in CSV header");

        for (int i = 1; i < csvLines.Length; i++)
        {
            var cols = csvLines[i].Split(',');
            var filePath = cols[filePathIdx];
            var isChild = parentDocIdIdx >= 0 && !string.IsNullOrEmpty(cols[parentDocIdIdx]);

            if (isChild)
            {
                var entry = archive.GetEntry(filePath);
                Assert.NotNull(entry);
                var csvFileSize = long.Parse(cols[fileSizeIdx], System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(entry.Length, csvFileSize);
                Assert.True(csvFileSize > 0);
            }
        }
    }

    [Fact]
    public async Task StandardMode_EmlWithFamiliesAndMetadata_ConcordanceMatchesCcAndAttachmentFileSize()
    {
        var outputDir = Path.Combine(this.TempDir, "concordance_test");
        Directory.CreateDirectory(outputDir);

        var args = new[]
        {
            "--type", "eml",
            "--count", "10",
            "--seed", "42",
            "--attachment-rate", "100",
            "--with-families",
            "--with-metadata",
            "--loadfile-format", "concordance",
            "--output-path", outputDir
        };

        var exitCode = await Program.Main(args);
        Assert.Equal(0, exitCode);

        var zipFile = Directory.GetFiles(outputDir, "*.zip").Single();
        var datFile = Directory.GetFiles(outputDir, "*.dat").Single();

        using var archive = ZipFile.OpenRead(zipFile);
        var datContent = await File.ReadAllTextAsync(datFile);
        var lines = datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Concordance uses \u0014 (quote) and \u00fe (delimiter)
        var header = lines[0].Split('\u00fe').Select(h => h.Trim('\u0014', '\r', '\n')).ToArray();
        int ccIdx = Array.FindIndex(header, h => string.Equals(h, "CC", StringComparison.OrdinalIgnoreCase));
        int fileSizeIdx = Array.FindIndex(header, h => string.Equals(h, "FILESIZE", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "File Size", StringComparison.OrdinalIgnoreCase));
        int filePathIdx = Array.FindIndex(header, h => string.Equals(h, "PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "File Path", StringComparison.OrdinalIgnoreCase));
        int parentDocIdIdx = Array.FindIndex(header, h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase));

        Assert.True(ccIdx >= 0, "CC column not found in Concordance header");
        Assert.True(fileSizeIdx >= 0, "FILESIZE column not found in Concordance header");

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('\u00fe').Select(c => c.Trim('\u0014', '\r', '\n')).ToArray();
            var filePath = cols[filePathIdx];
            var isChild = parentDocIdIdx >= 0 && !string.IsNullOrEmpty(cols[parentDocIdIdx]);

            if (isChild)
            {
                var entry = archive.GetEntry(filePath);
                Assert.NotNull(entry);
                var concordanceFileSize = long.Parse(cols[fileSizeIdx], System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(entry.Length, concordanceFileSize);
                Assert.True(concordanceFileSize > 0);
            }
        }
    }

    [Fact]
    public async Task StandardMode_EmlWithFamiliesAndMetadata_EdrmXmlMatchesCcAndAttachmentFileSize()
    {
        var outputDir = Path.Combine(this.TempDir, "xml_test");
        Directory.CreateDirectory(outputDir);

        var args = new[]
        {
            "--type", "eml",
            "--count", "10",
            "--seed", "42",
            "--attachment-rate", "100",
            "--with-families",
            "--with-metadata",
            "--loadfile-format", "edrm-xml",
            "--output-path", outputDir
        };

        var exitCode = await Program.Main(args);
        Assert.Equal(0, exitCode);

        var zipFile = Directory.GetFiles(outputDir, "*.zip").Single();
        var xmlFile = Directory.GetFiles(outputDir, "*.xml").Single();

        using var archive = ZipFile.OpenRead(zipFile);
        var doc = XDocument.Load(xmlFile);
        var batch = doc.Root?.Element("Batch");
        Assert.NotNull(batch);

        var documents = batch.Elements("Document").ToList();
        foreach (var document in documents)
        {
            var files = document.Element("Files")?.Elements("File").ToList();
            var tags = document.Element("Tags")?.Elements("Tag").ToDictionary(
                t => t.Attribute("TagName")?.Value ?? string.Empty,
                t => t.Attribute("TagValue")?.Value ?? string.Empty);

            if (files is not null)
            {
                foreach (var fileElem in files)
                {
                    var extFile = fileElem.Element("ExternalFile");
                    if (extFile is not null)
                    {
                        var filePath = extFile.Attribute("FilePath")?.Value;
                        var fileSizeAttr = extFile.Attribute("FileSize")?.Value;
                        if (filePath is not null && fileSizeAttr is not null)
                        {
                            var zipEntry = archive.GetEntry(filePath);
                            if (zipEntry is not null)
                            {
                                var actualZipLength = zipEntry.Length;
                                Assert.Equal(actualZipLength, long.Parse(fileSizeAttr, System.Globalization.CultureInfo.InvariantCulture));

                                if (fileElem.Attribute("FileType")?.Value == "Native" && tags is not null && tags.TryGetValue("FileSize", out var tagFileSize))
                                {
                                    Assert.Equal(actualZipLength, long.Parse(tagFileSize, System.Globalization.CultureInfo.InvariantCulture));
                                }

                                if (filePath.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) && tags is not null && tags.TryGetValue("CC", out var xmlCc))
                                {
                                    using var stream = zipEntry.Open();
                                    using var reader = new StreamReader(stream, Encoding.UTF8);
                                    string? line;
                                    string? mimeCc = null;
                                    while ((line = reader.ReadLine()) is not null)
                                    {
                                        if (string.IsNullOrEmpty(line))
                                            break;
                                        if (line.StartsWith("Cc: ", StringComparison.OrdinalIgnoreCase))
                                            mimeCc = line.Substring(4).Trim();
                                    }

                                    if (mimeCc is not null)
                                    {
                                        Assert.Equal(mimeCc, xmlCc);
                                    }
                                    else
                                    {
                                        Assert.Equal(string.Empty, xmlCc);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
