using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace Zipper.Tests;

public class OfficeFileGeneratorTests
{
    [Fact]
    public void IsOfficeFormat_WithDocx_ShouldReturnTrue()
    {
        // Act
        var result = OfficeFileGenerator.IsOfficeFormat("docx");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsOfficeFormat_WithXlsx_ShouldReturnTrue()
    {
        // Act
        var result = OfficeFileGenerator.IsOfficeFormat("xlsx");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsOfficeFormat_WithPptx_ShouldReturnFalse()
    {
        // Act
        var result = OfficeFileGenerator.IsOfficeFormat("pptx");

        // Assert
        Assert.False(result); // PPTX is not yet implemented
    }

    [Fact]
    public void IsOfficeFormat_WithPdf_ShouldReturnFalse()
    {
        // Act
        var result = OfficeFileGenerator.IsOfficeFormat("pdf");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsOfficeFormat_WithUpperCaseExtension_ShouldReturnTrue()
    {
        // Act
        var result = OfficeFileGenerator.IsOfficeFormat("DOCX");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsOfficeFormat_WithMixedCaseExtension_ShouldReturnTrue()
    {
        // Act
        var result = OfficeFileGenerator.IsOfficeFormat("DocX");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GenerateDocx_ShouldReturnValidZipArchive()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result = OfficeFileGenerator.GenerateDocx(workItem);

        // Assert
        Assert.NotEmpty(result);

        // Verify it's a valid ZIP archive
        using var stream = new MemoryStream(result);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.NotEmpty(archive.Entries);
    }

    [Fact]
    public void GenerateDocx_ShouldContainRequiredEntries()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 42 };

        // Act
        var result = OfficeFileGenerator.GenerateDocx(workItem);

        // Assert
        using var stream = new MemoryStream(result);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entryNames = archive.Entries.Select(e => e.FullName).ToList();

        // DOCX files must contain these entries
        Assert.Contains("[Content_Types].xml", entryNames);
        Assert.Contains("_rels/.rels", entryNames);
        Assert.Contains("word/document.xml", entryNames);
    }

    [Fact]
    public void GenerateDocx_ShouldIncludeDocumentContent()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 123 };

        // Act
        var result = OfficeFileGenerator.GenerateDocx(workItem);

        // Assert
        using var stream = new MemoryStream(result);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var documentEntry = archive.GetEntry("word/document.xml");
        Assert.NotNull(documentEntry);

        using var documentStream = documentEntry!.Open();
        using var reader = new StreamReader(documentStream);
        var content = reader.ReadToEnd();

        // O(1): pre-computed content is identical for all files
        Assert.Contains("eDiscovery testing", content, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateDocx_WithDifferentIndices_ShouldReturnSamePrecomputedContent()
    {
        // Arrange
        var workItem1 = new FileWorkItem { Index = 1 };
        var workItem2 = new FileWorkItem { Index = 2 };

        // Act
        var result1 = OfficeFileGenerator.GenerateDocx(workItem1);
        var result2 = OfficeFileGenerator.GenerateDocx(workItem2);

        // Assert: O(1) pre-computed content is identical for all indices
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GenerateXlsx_ShouldReturnNonEmptyByteArray()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result = OfficeFileGenerator.GenerateXlsx(workItem);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GenerateXlsx_ShouldContainValidExcelData()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 5 };

        // Act
        var result = OfficeFileGenerator.GenerateXlsx(workItem);

        // Assert
        // Verify it's a valid ZIP archive (XLSX is a ZIP file)
        using var stream = new MemoryStream(result);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        // An XLSX is an OPC package: assert the workbook part really is workbook XML. ClosedXML
        // may emit it namespace-prefixed, so compare the root's local name.
        var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidOperationException("xl/workbook.xml not found");
        using (var workbookStream = workbookEntry.Open())
        {
            var workbook = XDocument.Load(workbookStream);
            var root = workbook.Root;
            Assert.NotNull(root);
            Assert.Equal("workbook", root.Name.LocalName);
        }

        // ...and that the worksheet's own cell text reaches the shared strings table. The
        // Control Number and description are workbook content here, not Load File values.
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml") ?? throw new InvalidOperationException("xl/sharedStrings.xml not found");
        using (var reader = new StreamReader(sharedStringsEntry.Open()))
        {
            var sharedStrings = reader.ReadToEnd();
            Assert.Contains("DOC00000001", sharedStrings, StringComparison.Ordinal);
            Assert.Contains("Sample document for eDiscovery testing", sharedStrings, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GenerateXlsx_WithDifferentIndices_ShouldReturnSamePrecomputedContent()
    {
        // Arrange
        var workItem1 = new FileWorkItem { Index = 1 };
        var workItem2 = new FileWorkItem { Index = 2 };

        // Act
        var result1 = OfficeFileGenerator.GenerateXlsx(workItem1);
        var result2 = OfficeFileGenerator.GenerateXlsx(workItem2);

        // Assert: O(1) pre-computed content is identical for all indices
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GenerateContent_WithDocx_ShouldCallGenerateDocx()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result = OfficeFileGenerator.GenerateContent("docx", workItem);

        // Assert
        Assert.NotEmpty(result);

        // Verify it's a valid DOCX package
        AssertOpcPartExists(result, "word/document.xml");
    }

    [Fact]
    public void GenerateContent_WithXlsx_ShouldCallGenerateXlsx()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result = OfficeFileGenerator.GenerateContent("xlsx", workItem);

        // Assert
        Assert.NotEmpty(result);

        // Verify it's a valid XLSX package
        AssertOpcPartExists(result, "xl/workbook.xml");
    }

    [Fact]
    public void GenerateContent_WithUnsupportedFormat_ShouldThrowArgumentException()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            OfficeFileGenerator.GenerateContent("pdf", workItem));
    }

    [Fact]
    public void GenerateContent_WithPptx_ShouldThrowNotImplementedException()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act & Assert
        Assert.Throws<System.NotImplementedException>(() =>
            OfficeFileGenerator.GenerateContent("pptx", workItem));
    }

    [Fact]
    public void GenerateContent_WithUpperCaseFileType_ShouldWork()
    {
        // Arrange
        var workItem = new FileWorkItem { Index = 1 };

        // Act
        var result = OfficeFileGenerator.GenerateContent("DOCX", workItem);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Constructor_ShouldSetFileType()
    {
        // Arrange & Act
        var generator = new OfficeFileGenerator("docx");

        // Assert
        Assert.Equal("docx", generator.FileType);
    }

    [Fact]
    public void IsPlaceholderBased_ShouldReturnFalse()
    {
        // Arrange
        var generator = new OfficeFileGenerator("docx");

        // Act & Assert
        Assert.False(generator.IsPlaceholderBased);
    }

    [Fact]
    public void Generate_WithDocx_ShouldReturnValidGeneratedFileContent()
    {
        // Arrange
        var generator = new OfficeFileGenerator("docx");
        var workItem = new FileWorkItem { Index = 1 };
        var request = new FileGenerationRequest();

        // Act
        var result = generator.Generate(workItem, request);

        // Assert
        Assert.NotEmpty(result.Content);

        AssertOpcPartExists(result.Content, "word/document.xml");
    }

    [Fact]
    public void Generate_WithXlsx_ShouldReturnValidGeneratedFileContent()
    {
        // Arrange
        var generator = new OfficeFileGenerator("xlsx");
        var workItem = new FileWorkItem { Index = 1 };
        var request = new FileGenerationRequest();

        // Act
        var result = generator.Generate(workItem, request);

        // Assert
        Assert.NotEmpty(result.Content);

        AssertOpcPartExists(result.Content, "xl/workbook.xml");
    }

    [Fact]
    public void Generate_ShouldMatchStaticGenerateContent()
    {
        // Arrange
        var generator = new OfficeFileGenerator("docx");
        var workItem = new FileWorkItem { Index = 5 };
        var request = new FileGenerationRequest();

        // Act
        var instanceResult = generator.Generate(workItem, request);
        var staticResult = OfficeFileGenerator.GenerateContent("docx", workItem);

        // Assert
        Assert.Equal(staticResult, instanceResult.Content);
    }

    /// <summary>
    /// Asserts that a generated Office package actually contains the OPC part its format
    /// requires. Asserting on the part is what makes the archive check meaningful: the
    /// <see cref="ZipArchive"/> constructor throws on non-archive bytes and never returns null,
    /// so a nullness assertion on the archive itself verifies nothing.
    /// </summary>
    private static void AssertOpcPartExists(byte[] package, string partPath)
    {
        using var stream = new MemoryStream(package);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry(partPath));
    }
}
