using System.IO.Compression;
using Xunit;

namespace Zipper
{
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
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            // Verify it's a valid ZIP archive
            using var stream = new MemoryStream(result);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            Assert.NotNull(archive);
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

            Assert.Contains("Document 123", content);
            Assert.Contains("DOC00000123", content);
        }

        [Fact]
        public void GenerateDocx_WithDifferentIndices_ShouldProduceDifferentContent()
        {
            // Arrange
            var workItem1 = new FileWorkItem { Index = 1 };
            var workItem2 = new FileWorkItem { Index = 2 };

            // Act
            var result1 = OfficeFileGenerator.GenerateDocx(workItem1);
            var result2 = OfficeFileGenerator.GenerateDocx(workItem2);

            // Assert
            Assert.NotEqual(result1, result2);
        }

        [Fact]
        public void GenerateXlsx_ShouldReturnNonEmptyByteArray()
        {
            // Arrange
            var workItem = new FileWorkItem { Index = 1 };

            // Act
            var result = OfficeFileGenerator.GenerateXlsx(workItem);

            // Assert
            Assert.NotNull(result);
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
            Assert.NotNull(archive);

            // XLSX files should contain workbook entries
            var xlEntry = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/"));
            Assert.NotNull(xlEntry);
        }

        [Fact]
        public void GenerateXlsx_WithDifferentIndices_ShouldProduceDifferentFiles()
        {
            // Arrange
            var workItem1 = new FileWorkItem { Index = 1 };
            var workItem2 = new FileWorkItem { Index = 2 };

            // Act
            var result1 = OfficeFileGenerator.GenerateXlsx(workItem1);
            var result2 = OfficeFileGenerator.GenerateXlsx(workItem2);

            // Assert
            Assert.NotEqual(result1, result2);
        }

        [Fact]
        public void GenerateContent_WithDocx_ShouldCallGenerateDocx()
        {
            // Arrange
            var workItem = new FileWorkItem { Index = 1 };

            // Act
            var result = OfficeFileGenerator.GenerateContent("docx", workItem);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            // Verify it's a valid ZIP archive
            using var stream = new MemoryStream(result);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            Assert.NotNull(archive);
        }

        [Fact]
        public void GenerateContent_WithXlsx_ShouldCallGenerateXlsx()
        {
            // Arrange
            var workItem = new FileWorkItem { Index = 1 };

            // Act
            var result = OfficeFileGenerator.GenerateContent("xlsx", workItem);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            // Verify it's a valid ZIP archive
            using var stream = new MemoryStream(result);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            Assert.NotNull(archive);
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
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }
    }
}
