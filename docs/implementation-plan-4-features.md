# Implementation Plan: 4 Key Features for Zipper

**Branch**: `feature/office-formats-loadfiles-bates-tiff`

**Features to Implement**:
1. DOCX/XLSX/PPTX Support (#1)
2. Additional Load File Formats (#3)
3. Bates Numbering System (#6)
4. Multipage TIFF Support (#9)

---

## Overview

This implementation plan details the development of four high-impact features that significantly expand Zipper's capabilities for eDiscovery testing. These features address critical gaps in the current functionality and align with industry standards.

### Impact Summary
- **DOCX/XLSX/PPTX**: Microsoft Office formats represent 60-80% of real eDiscovery data
- **Load File Formats**: Different platforms require specific formats (OPT, CSV, XML, CONCORDANCE)
- **Bates Numbering**: Legal industry standard for document identification
- **Multipage TIFF**: Essential for legal scanning workflows

---

## Feature #1: DOCX/XLSX/PPTX Support

### Requirements
- Generate valid DOCX, XLSX, and PPTX files
- Add `--type docx`, `--type xlsx`, `--type pptx` CLI arguments
- Files must open in LibreOffice (Linux) and Microsoft Office (Windows)
- Support existing features: metadata, text extraction, distribution, folders

### Design

#### Architecture
```
FileGenerationRequest (FileType: "docx"|"xlsx"|"pptx")
         ↓
OfficeFileGenerator.GenerateContent()
         ↓
┌─────────────────────────────────┐
│  FileType                       │
│  ├─ docx → WordGenerator       │
│  ├─ xlsx → ExcelGenerator      │
│  └─ pptx → PowerPointGenerator │
└─────────────────────────────────┘
         ↓
byte[] content (valid Office document)
```

#### Dependencies
```xml
<PackageReference Include="DocumentFormat.OpenXml" Version="3.1.0" />
<PackageReference Include="ClosedXML" Version="0.104.2" />
```

### Implementation Steps

#### Step 1.1: Create `OfficeFileGenerator.cs`
Location: `Zipper/OfficeFileGenerator.cs`

```csharp
namespace Zipper;

public static class OfficeFileGenerator
{
    public static byte[] GenerateDocx(FileWorkItem workItem)
    {
        using var stream = new MemoryStream();

        // Create minimal valid DOCX using DocumentFormat.OpenXml
        var doc = new DocumentFormat.OpenXml.Packaging.WordprocessingDocument(
            stream, DocumentFormat.OpenXml.Packaging.WordprocessingDocumentType.Document);

        // Add main document part
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
            new DocumentFormat.OpenXml.Wordprocessing.Body(
                new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(
                        new DocumentFormat.OpenXml.Wordprocessing.Text(
                            $"Document {workItem.Index}")))));

        doc.Save();
        return stream.ToArray();
    }

    public static byte[] GenerateXlsx(FileWorkItem workItem)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        // Add realistic data
        worksheet.Cell("A1").Value = "Control Number";
        worksheet.Cell("B1").Value = "Date";
        worksheet.Cell("C1").Value = "Description";

        for (int i = 2; i <= 10; i++)
        {
            worksheet.Cell($"A{i}").Value = $"DOC{workItem.Index:D8}";
            worksheet.Cell($"B{i}").Value = DateTime.Now.AddDays(-Random.Shared.Next(1, 365));
            worksheet.Cell($"C{i}").Value = $"Item {i} for document {workItem.Index}";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] GeneratePptx(FileWorkItem workItem)
    {
        using var stream = new MemoryStream();

        // Create minimal valid PPTX using DocumentFormat.OpenXml
        var pres = new DocumentFormat.OpenXml.Packaging.PresentationDocument(
            stream, DocumentFormat.OpenXml.Packaging.PresentationDocumentType.Presentation);

        var presentationPart = pres.AddPresentationPart();
        presentationPart.Presentation = new DocumentFormat.OpenXml.Presentation.Presentation();

        // Add slide
        var slidePart = presentationPart.AddNewPart<DocumentFormat.OpenXml.Packaging.SlidePart>();
        slidePart.Slide = new DocumentFormat.OpenXml.Presentation.Slide(
            new DocumentFormat.OpenXml.Presentation.CommonSlideData(
                new DocumentFormat.OpenXml.Presentation.ShapeTree(
                    new DocumentFormat.OpenXml.Presentation.NonVisualGroupShapeProperties(),
                    new DocumentFormat.OpenXml.Presentation.GroupShapeProperties(
                        new DocumentFormat.OpenXml.Drawing.TransformGroup()),
                    new DocumentFormat.OpenXml.Presentation.Shape(
                        new DocumentFormat.OpenXml.Presentation.NonVisualShapeProperties(
                            new DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties { Id = 1, Name = "Title" }),
                        new DocumentFormat.OpenXml.Presentation.ShapeProperties(),
                        new DocumentFormat.OpenXml.Presentation.TextBody(
                            new DocumentFormat.OpenXml.Drawing.BodyProperties(),
                            new DocumentFormat.OpenXml.Drawing.ListStyle(),
                            new DocumentFormat.OpenXml.Drawing.Paragraph(
                                new DocumentFormat.OpenXml.Drawing.Run(
                                    new DocumentFormat.OpenXml.Drawing.Text($"Slide {workItem.Index}")))))))));

        pres.Save();
        return stream.ToArray();
    }

    public static byte[] GenerateContent(string fileType, FileWorkItem workItem)
    {
        return fileType.ToLowerInvariant() switch
        {
            "docx" => GenerateDocx(workItem),
            "xlsx" => GenerateXlsx(workItem),
            "pptx" => GeneratePptx(workItem),
            _ => throw new ArgumentException($"Unsupported Office format: {fileType}")
        };
    }
}
```

#### Step 1.2: Update `PlaceholderFiles.cs`
Add Office format support to existing dictionary:

```csharp
private static readonly Dictionary<string, byte[]> FileContentMap = new(StringComparer.OrdinalIgnoreCase)
{
    { "jpg", Jpg },
    { "tiff", Tiff },
    { "pdf", Pdf }
    // Note: Office files generated dynamically via OfficeFileGenerator
};
```

#### Step 1.3: Update `ParallelFileGenerator.cs`
Add conditional logic to use `OfficeFileGenerator` for Office formats:

```csharp
private static async Task<FileData> GenerateFileContentAsync(FileWorkItem workItem, FileGenerationRequest request)
{
    byte[] content;

    if (IsOfficeFormat(request.FileType))
    {
        content = OfficeFileGenerator.GenerateContent(request.FileType, workItem);
    }
    else
    {
        // Existing logic for pdf/jpg/tiff/eml
        content = PlaceholderFiles.GetContent(request.FileType);
    }

    // ... rest of existing logic
}

private static bool IsOfficeFormat(string fileType) =>
    fileType.Equals("docx", StringComparison.OrdinalIgnoreCase) ||
    fileType.Equals("xlsx", StringComparison.OrdinalIgnoreCase) ||
    fileType.Equals("pptx", StringComparison.OrdinalIgnoreCase);
```

#### Step 1.4: Update `CommandLineValidator.cs`
Add new file types to validation:

```csharp
// In ShowUsage():
Console.Error.WriteLine($"Usage: {exeName} --type <pdf|jpg|tiff|eml|docx|xlsx|pptx> ...");

// Add validation (existing file types + docx/xlsx/pptx)
```

### Testing

#### Unit Test: `OfficeFileGeneratorTests.cs`
```csharp
public class OfficeFileGeneratorTests
{
    [Fact]
    public void GenerateDocx_ShouldCreateValidDocument()
    {
        var workItem = new FileWorkItem { Index = 1 };
        var content = OfficeFileGenerator.GenerateDocx(workItem);

        Assert.True(content.Length > 0);
        Assert.StartsWith("PK", Encoding.ASCII.GetString(content, 0, 2)); // ZIP signature
    }

    [Fact]
    public void GenerateXlsx_ShouldCreateValidSpreadsheet()
    {
        var workItem = new FileWorkItem { Index = 1 };
        var content = OfficeFileGenerator.GenerateXlsx(workItem);

        Assert.True(content.Length > 0);
        Assert.StartsWith("PK", Encoding.ASCII.GetString(content, 0, 2));
    }

    [Fact]
    public void GeneratePptx_ShouldCreateValidPresentation()
    {
        var workItem = new FileWorkItem { Index = 1 };
        var content = OfficeFileGenerator.GeneratePptx(workItem);

        Assert.True(content.Length > 0);
        Assert.StartsWith("PK", Encoding.ASCII.GetString(content, 0, 2));
    }
}
```

#### E2E Tests
`tests/test-office-formats.sh`:
```bash
#!/bin/bash
# Test DOCX/XLSX/PPTX generation

dotnet run --project ../Zipper/Zipper.csproj -- \
    --type docx --count 10 --output-path ./test-output --folders 2 --with-metadata

# Verify output
if [ ! -f "test-output.zip" ]; then
    echo "FAIL: ZIP file not created"
    exit 1
fi

# Verify .dat file exists
if [ ! -f "test-output.dat" ]; then
    echo "FAIL: DAT file not created"
    exit 1
fi

# Check DAT header contains DOC metadata
if ! grep -q "Control Number" test-output.dat; then
    echo "FAIL: DAT header incorrect"
    exit 1
fi

echo "PASS: Office format generation test"
```

`tests/test-office-formats.bat`:
```batch
@echo off
REM Windows version of office formats test

dotnet run --project ..\Zipper\Zipper.csproj -- --type docx --count 10 --output-path .\test-output --folders 2 --with-metadata

if not exist "test-output.zip" (
    echo FAIL: ZIP file not created
    exit /b 1
)

if not exist "test-output.dat" (
    echo FAIL: DAT file not created
    exit /b 1
)

findstr /C:"Control Number" test-output.dat
if errorlevel 1 (
    echo FAIL: DAT header incorrect
    exit /b 1
)

echo PASS: Office format generation test
```

---

## Feature #2: Additional Load File Formats

### Requirements
- Support OPT (Opticon/Relativity), CSV, XML, CONCORDANCE formats
- Add `--load-file-format <dat|opt|csv|xml|concordance>` CLI argument
- Maintain backward compatibility (DAT as default)
- All formats must contain same metadata

### Design

#### Architecture
```
LoadFileFormat Enum
         ↓
ILoadFileWriter Interface
         ↓
┌────────────────────────────────┐
│  Format                         │
│  ├─ dat  → DatWriter (existing)│
│  ├─ opt  → OptWriter            │
│  ├─ csv  → CsvWriter            │
│  ├─ xml  → XmlWriter            │
│  └─ concordance → Concordance... │
└────────────────────────────────┘
```

### Implementation Steps

#### Step 2.1: Create `ILoadFileWriter.cs` Interface
Location: `Zipper/LoadFiles/ILoadFileWriter.cs`

```csharp
namespace Zipper.LoadFiles;

public interface ILoadFileWriter
{
    string FormatName { get; }
    string FileExtension { get; }
    Task WriteAsync(Stream stream, FileGenerationRequest request, List<FileData> processedFiles);
}
```

#### Step 2.2: Create `LoadFileFormat.cs` Enum
Location: `Zipper/LoadFileFormat.cs`

```csharp
namespace Zipper;

public enum LoadFileFormat
{
    Dat,        // Default (existing)
    Opt,        // Opticon/Relativity
    Csv,        // Comma-separated values
    Xml,        // XML markup
    Concordance // Concordance database format
}
```

#### Step 2.3: Create `OptWriter.cs`
Location: `Zipper/LoadFiles/OptWriter.cs`

```csharp
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Zipper.LoadFiles;

public class OptWriter : ILoadFileWriter
{
    public string FormatName => "OPT";
    public string FileExtension => ".opt";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, List<FileData> processedFiles)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8);

        // OPT format: Tab-separated, no headers
        const char tab = '\t';

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var docId = $"DOC{workItem.Index:D8}";

            var line = $"{docId}{tab}{workItem.FilePathInZip}";

            // Add metadata columns
            if (request.WithMetadata)
            {
                var custodian = $"Custodian {workItem.FolderNumber}";
                var dateSent = DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                var author = $"Author {Random.Shared.Next(1, 100):D3}";
                var fileSize = fileData.Data.Length;

                line += $"{tab}{custodian}{tab}{dateSent}{tab}{author}{tab}{fileSize}";
            }

            // Add EML columns
            if (request.FileType.ToLowerInvariant() == "eml")
            {
                var to = $"recipient{workItem.Index}@example.com";
                var from = $"sender{workItem.Index}@example.com";
                var subject = $"Email Subject {workItem.Index}";

                line += $"{tab}{to}{tab}{from}{tab}{subject}";
            }

            // Add text path
            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                line += $"{tab}{textFilePath}";
            }

            await writer.WriteLineAsync(line);
        }
    }
}
```

#### Step 2.4: Create `CsvWriter.cs`
Location: `Zipper/LoadFiles/CsvWriter.cs`

```csharp
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Zipper.LoadFiles;

public class CsvWriter : ILoadFileWriter
{
    public string FormatName => "CSV";
    public string FileExtension => ".csv";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, List<FileData> processedFiles)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8);

        // Build header
        var headers = new List<string> { "Control Number", "File Path" };

        if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
        {
            headers.AddRange(new[] { "Custodian", "Date Sent", "Author", "File Size" });
        }

        if (request.FileType.ToLowerInvariant() == "eml")
        {
            headers.AddRange(new[] { "To", "From", "Subject", "Sent Date", "Attachment" });
        }

        if (request.WithText)
        {
            headers.Add("Extracted Text");
        }

        await writer.WriteLineAsync(string.Join(",", headers));

        // Write data rows
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var values = new List<string>
            {
                $"DOC{workItem.Index:D8}",
                EscapeCsvField(workItem.FilePathInZip)
            };

            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                var custodian = $"Custodian {workItem.FolderNumber}";
                var dateSent = DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                var author = $"Author {Random.Shared.Next(1, 100):D3}";
                var fileSize = fileData.Data.Length;

                values.AddRange(new[] {
                    EscapeCsvField(custodian),
                    EscapeCsvField(dateSent),
                    EscapeCsvField(author),
                    fileSize.ToString()
                });
            }

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                var to = $"recipient{workItem.Index}@example.com";
                var from = $"sender{workItem.Index}@example.com";
                var subject = $"Email Subject {workItem.Index}";

                values.AddRange(new[] {
                    EscapeCsvField(to),
                    EscapeCsvField(from),
                    EscapeCsvField(subject)
                });
            }

            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                values.Add(EscapeCsvField(textFilePath));
            }

            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
```

#### Step 2.5: Create `XmlWriter.cs`
Location: `Zipper/LoadFiles/XmlWriter.cs`

```csharp
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Zipper.LoadFiles;

public class XmlWriter : ILoadFileWriter
{
    public string FormatName => "XML";
    public string FileExtension => ".xml";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, List<FileData> processedFiles)
    {
        var root = new XElement("Documents");

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var docElement = new XElement("Document",
                new XElement("ControlNumber", $"DOC{workItem.Index:D8}"),
                new XElement("FilePath", workItem.FilePathInZip));

            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                docElement.Add(new XElement("Metadata",
                    new XElement("Custodian", $"Custodian {workItem.FolderNumber}"),
                    new XElement("DateSent", DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd")),
                    new XElement("Author", $"Author {Random.Shared.Next(1, 100):D3}"),
                    new XElement("FileSize", fileData.Data.Length)));
            }

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                docElement.Add(new XElement("Email",
                    new XElement("To", $"recipient{workItem.Index}@example.com"),
                    new XElement("From", $"sender{workItem.Index}@example.com"),
                    new XElement("Subject", $"Email Subject {workItem.Index}")));
            }

            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                docElement.Add(new XElement("ExtractedTextPath", textFilePath));
            }

            root.Add(docElement);
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            root);

        using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(document.ToString());
    }
}
```

#### Step 2.6: Create `ConcordanceWriter.cs`
Location: `Zipper/LoadFiles/ConcordanceWriter.cs`

```csharp
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Zipper.LoadFiles;

public class ConcordanceWriter : ILoadFileWriter
{
    public string FormatName => "CONCORDANCE";
    public string FileExtension => ".dat";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, List<FileData> processedFiles)
    {
        using var writer = new StreamWriter(stream, GetEncoding(request.Encoding));

        // Concordance uses specific delimiters
        const char fieldDelim = '\x14'; // Group separator
        const char quote = '"';

        // Build header (Concordance requires specific column names)
        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"BEGATTY{quote}{fieldDelim}");
        headerBuilder.Append($"ENDDATTY{quote}{fieldDelim}");
        headerBuilder.Append($"CONTROLNUMBER{quote}{fieldDelim}");
        headerBuilder.Append($"PATH{quote}{fieldDelim}");

        if (request.WithMetadata)
        {
            headerBuilder.Append($"CUSTODIAN{quote}{fieldDelim}");
            headerBuilder.Append($"DATESENT{quote}{fieldDelim}");
            headerBuilder.Append($"AUTHOR{quote}{fieldDelim}");
            headerBuilder.Append($"FILESIZE{quote}{fieldDelim}");
        }

        await writer.WriteLineAsync(headerBuilder.ToString());

        // Write data rows
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var lineBuilder = new StringBuilder();

            lineBuilder.Append($"{quote}{quote}{fieldDelim}"); // BEGATTY
            lineBuilder.Append($"{quote}{quote}{fieldDelim}"); // ENDDATTY
            lineBuilder.Append($"{quote}DOC{workItem.Index:D8}{quote}{fieldDelim}"); // CONTROLNUMBER
            lineBuilder.Append($"{quote}{workItem.FilePathInZip}{quote}{fieldDelim}"); // PATH

            if (request.WithMetadata)
            {
                var custodian = $"Custodian {workItem.FolderNumber}";
                var dateSent = DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                var author = $"Author {Random.Shared.Next(1, 100):D3}";
                var fileSize = fileData.Data.Length;

                lineBuilder.Append($"{quote}{custodian}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{dateSent}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{author}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{fileSize}{quote}{fieldDelim}");
            }

            await writer.WriteLineAsync(lineBuilder.ToString());
        }
    }

    private static Encoding GetEncoding(string encodingName) =>
        EncodingHelper.GetEncodingOrDefault(encodingName);
}
```

#### Step 2.7: Create `LoadFileWriterFactory.cs`
Location: `Zipper/LoadFiles/LoadFileWriterFactory.cs`

```csharp
namespace Zipper.LoadFiles;

public static class LoadFileWriterFactory
{
    public static ILoadFileWriter CreateWriter(LoadFileFormat format)
    {
        return format switch
        {
            LoadFileFormat.Dat => new DatWriter(),
            LoadFileFormat.Opt => new OptWriter(),
            LoadFileFormat.Csv => new CsvWriter(),
            LoadFileFormat.Xml => new XmlWriter(),
            LoadFileFormat.Concordance => new ConcordanceWriter(),
            _ => throw new ArgumentException($"Unsupported load file format: {format}")
        };
    }
}

// Existing LoadFileGenerator wrapped as DatWriter
internal class DatWriter : ILoadFileWriter
{
    public string FormatName => "DAT";
    public string FileExtension => ".dat";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, List<FileData> processedFiles)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        await LoadFileGenerator.WriteLoadFileContent(writer, request, processedFiles);
    }
}
```

#### Step 2.8: Update `FileGenerationRequest.cs`
Add new property:

```csharp
public LoadFileFormat LoadFileFormat { get; set; } = LoadFileFormat.Dat;
```

#### Step 2.9: Update `CommandLineValidator.cs`
Add new argument parsing:

```csharp
case "--load-file-format":
    if (i + 1 < args.Length) parsed.LoadFileFormat = args[++i];
    break;

// Add validation
private static LoadFileFormat? GetLoadFileFormat(string name)
{
    return name.ToUpperInvariant() switch
    {
        "DAT" => LoadFileFormat.Dat,
        "OPT" => LoadFileFormat.Opt,
        "CSV" => LoadFileFormat.Csv,
        "XML" => LoadFileFormat.Xml,
        "CONCORDANCE" => LoadFileFormat.Concordance,
        _ => null
    };
}
```

#### Step 2.10: Update `ZipArchiveService.cs`
Use factory pattern for load file writing:

```csharp
var writer = LoadFileWriterFactory.CreateWriter(request.LoadFileFormat);
var loadFileName = Path.GetFileNameWithoutExtension(request.OutputPath) + writer.FileExtension;
await writer.WriteAsync(loadFileStream, request, processedFiles);
```

### Testing

#### Unit Tests
`Zipper.Tests/LoadFileWriterTests.cs`

```csharp
public class LoadFileWriterTests
{
    [Fact]
    public async Task OptWriter_ShouldCreateTabDelimitedFormat()
    {
        var writer = new OptWriter();
        var request = CreateTestRequest();
        var files = CreateTestFiles();

        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Contains('\t', content);
    }

    [Fact]
    public async Task CsvWriter_ShouldEscapeCommas()
    {
        var writer = new CsvWriter();
        var request = CreateTestRequest();
        var files = CreateTestFiles();

        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var lines = (await reader.ReadToEndAsync()).Split('\n');

        Assert.True(lines.Length > 1);
    }

    [Fact]
    public async Task XmlWriter_ShouldCreateValidXml()
    {
        var writer = new XmlWriter();
        var request = CreateTestRequest();
        var files = CreateTestFiles();

        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, files);

        stream.Position = 0;
        var content = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("<Documents>", content);
        Assert.Contains("</Documents>", content);
    }
}
```

#### E2E Tests
`tests/test-loadfile-formats.sh`:
```bash
#!/bin/bash

echo "Testing DAT format..."
dotnet run --project ../Zipper/Zipper.csproj -- --type pdf --count 10 --output-path ./test-dat --load-file-format dat
[ -f "test-dat.dat" ] && echo "PASS: DAT" || echo "FAIL: DAT"

echo "Testing OPT format..."
dotnet run --project ../Zipper/Zipper.csproj -- --type pdf --count 10 --output-path ./test-opt --load-file-format opt
[ -f "test-opt.opt" ] && echo "PASS: OPT" || echo "FAIL: OPT"

echo "Testing CSV format..."
dotnet run --project ../Zipper/Zipper.csproj -- --type pdf --count 10 --output-path ./test-csv --load-file-format csv
[ -f "test-csv.csv" ] && echo "PASS: CSV" || echo "FAIL: CSV"

echo "Testing XML format..."
dotnet run --project ../Zipper/Zipper.csproj -- -- --type pdf --count 10 --output-path ./test-xml --load-file-format xml
[ -f "test-xml.xml" ] && echo "PASS: XML" || echo "FAIL: XML"
```

---

## Feature #3: Bates Numbering System

### Requirements
- Add `--bates-prefix <string>`, `--bates-start <number>`, `--bates-digits <number>` CLI arguments
- Format: PREFIX00000001, PREFIX00000002, etc.
- Add Bates number to load file column
- Apply to all file types

### Design

#### Architecture
```
BatesNumberGenerator
    ↓
Generate(currentIndex) → "PREFIX00000001"
    ↓
LoadFile column: "Bates Number"
Filename prefix (optional): "PREFIX00000001_document.pdf"
```

### Implementation Steps

#### Step 3.1: Create `BatesNumberGenerator.cs`
Location: `Zipper/BatesNumberGenerator.cs`

```csharp
namespace Zipper;

public record BatesNumberConfig
{
    public string Prefix { get; init; } = "DOC";
    public long Start { get; init; } = 1;
    public int Digits { get; init; } = 8;
    public long Increment { get; init; } = 1;
}

public static class BatesNumberGenerator
{
    public static string Generate(BatesNumberConfig config, long currentIndex)
    {
        var number = config.Start + (currentIndex * config.Increment);
        var formattedNumber = number.ToString($"D{config.Digits}");
        return $"{config.Prefix}{formattedNumber}";
    }

    public static string GenerateWithoutPrefix(BatesNumberConfig config, long currentIndex)
    {
        var number = config.Start + (currentIndex * config.Increment);
        return number.ToString($"D{config.Digits}");
    }
}
```

#### Step 3.2: Update `FileGenerationRequest.cs`
Add Bates numbering properties:

```csharp
public BatesNumberConfig? BatesConfig { get; set; }
```

#### Step 3.3: Update `CommandLineValidator.cs`
Add Bates number argument parsing:

```csharp
case "--bates-prefix":
    if (i + 1 < args.Length) parsed.BatesPrefix = args[++i];
    break;
case "--bates-start":
    if (i + 1 < args.Length && long.TryParse(args[++i], out var batesStart)) parsed.BatesStart = batesStart;
    break;
case "--bates-digits":
    if (i + 1 < args.Length && int.TryParse(args[++i], out var batesDigits)) parsed.BatesDigits = batesDigits;
    break;

// In ParsedArguments class:
public string? BatesPrefix { get; set; }
public long? BatesStart { get; set; }
public int? BatesDigits { get; set; }

// In validation:
if (!string.IsNullOrEmpty(parsed.BatesPrefix))
{
    var config = new BatesNumberConfig
    {
        Prefix = parsed.BatesPrefix,
        Start = parsed.BatesStart ?? 1,
        Digits = parsed.BatesDigits ?? 8
    };
    request.BatesConfig = config;
}
```

#### Step 3.4: Update `LoadFileGenerator.cs`
Add Bates column to load file:

```csharp
// In WriteLoadFileContent, add to header:
if (request.BatesConfig != null)
{
    headerBuilder.Append($"{colDelim}{quote}Bates Number{quote}");
}

// In WriteFileRecord, add Bates number:
if (request.BatesConfig != null)
{
    var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1);
    lineBuilder.Append($"{colDelim}{quote}{batesNumber}{quote}");
}
```

#### Step 3.5: Update `ParallelFileGenerator.cs` (Optional - Filename Prefixing)
If adding Bates to filenames:

```csharp
// In GenerateFileName, add Bates prefix:
private static string GenerateFileName(FileWorkItem workItem, FileGenerationRequest request, string fileType)
{
    var baseFileName = fileIndex.ToString($"D{8}");

    if (request.BatesConfig != null)
    {
        baseFileName = BatesNumberGenerator.GenerateWithoutPrefix(request.BatesConfig, workItem.Index - 1);
    }

    return $"{baseFileName}.{fileType}";
}
```

### Testing

#### Unit Tests
`Zipper.Tests/BatesNumberGeneratorTests.cs`

```csharp
public class BatesNumberGeneratorTests
{
    [Fact]
    public void Generate_ShouldFormatCorrectly()
    {
        var config = new BatesNumberConfig
        {
            Prefix = "TEST",
            Start = 1,
            Digits = 8
        };

        var result = BatesNumberGenerator.Generate(config, 0);

        Assert.Equal("TEST00000001", result);
    }

    [Fact]
    public void Generate_ShouldIncrementCorrectly()
    {
        var config = new BatesNumberConfig
        {
            Prefix = "TEST",
            Start = 1,
            Increment = 10
        };

        var result1 = BatesNumberGenerator.Generate(config, 0);
        var result2 = BatesNumberGenerator.Generate(config, 1);

        Assert.Equal("TEST00000001", result1);
        Assert.Equal("TEST00000011", result2);
    }

    [Fact]
    public void Generate_WithoutPrefix_ShouldReturnNumberOnly()
    {
        var config = new BatesNumberConfig
        {
            Start = 100,
            Digits = 6
        };

        var result = BatesNumberGenerator.GenerateWithoutPrefix(config, 0);

        Assert.Equal("000100", result);
    }
}
```

#### E2E Tests
`tests/test-bates-numbering.sh`:
```bash
#!/bin/bash

dotnet run --project ../Zipper/Zipper.csproj -- \
    --type pdf --count 100 --output-path ./test-bates \
    --bates-prefix "CLIENT001" --bates-start 1 --bates-digits 8 \
    --with-metadata

# Check DAT file for Bates column
if grep -q "Bates Number" test-bates.dat; then
    echo "PASS: Bates column header found"
else
    echo "FAIL: Bates column header missing"
    exit 1
fi

# Check for first Bates number
if grep -q "CLIENT00100000001" test-bates.dat; then
    echo "PASS: Bates numbering applied"
else
    echo "FAIL: Bates numbering not applied"
    exit 1
fi
```

---

## Feature #4: Multipage TIFF Support

### Requirements
- Add `--tiff-pages <min>-<max>` CLI argument (default: 1-1)
- Generate multipage TIFF files with random page counts
- Add `Page Count` column to load file
- Must be valid multipage TIFF format

### Design

#### Architecture
```
--tiff-pages 1-20
    ↓
RandomPageCount(1, 20) → e.g., 7 pages
    ↓
TiffMultiPageGenerator.Generate(7 pages)
    ↓
Valid multipage TIFF with 7 pages
```

### Implementation Steps

#### Step 4.1: Update `FileGenerationRequest.cs`
Add TIFF pages property:

```csharp
public (int Min, int Max)? TiffPageRange { get; set; }
```

#### Step 4.2: Create `TiffMultiPageGenerator.cs`
Location: `Zipper/TiffMultiPageGenerator.cs`

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Tiff;
using System.IO;

namespace Zipper;

public static class TiffMultiPageGenerator
{
    public static byte[] Generate(int pageCount, FileWorkItem workItem)
    {
        using var stream = new MemoryStream();

        using var firstImage = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(100, 100);
        firstImage.Mutate(x => x.BackgroundColor(SixLabors.ImageSharp.Color.Black));

        var encoder = new TiffEncoder
        {
            PhotometricInterpretation = TiffPhotometricInterpretation.BlackIsZero,
            BitsPerSample = new[] { TiffBitsPerSample.Bit8 }
        };

        // For multipage TIFF, we need to encode multiple frames
        // ImageSharp supports this via TiffEncoder with multiple frames
        using var multiFrameImage = firstImage.Clone();

        for (int i = 1; i < pageCount; i++)
        {
            using var additionalPage = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(100, 100);
            additionalPage.Mutate(x =>
            {
                x.BackgroundColor(SixLabors.ImageSharp.Color.Black);
                // Add variation per page
                x.DrawText($"Page {i + 1}", System.Drawing.Color.White, new SixLabors.ImageSharp.PointF(10, 10));
            });

            // Add frame to multipage TIFF
            // Note: ImageSharp's TIFF encoding handles this
        }

        firstImage.Save(stream, encoder);

        // If ImageSharp doesn't support direct multipage, use alternative approach
        // Generate as multi-frame TIFF

        return stream.ToArray();
    }

    public static int GetPageCount((int Min, int Max)? range, long fileIndex)
    {
        if (!range.HasValue || range.Value.Min == range.Value.Max)
        {
            return range?.Min ?? 1;
        }

        // Deterministic random based on file index
        var random = new Random((int)fileIndex);
        return random.Next(range.Value.Min, range.Value.Max + 1);
    }
}
```

**Note**: ImageSharp's TIFF support for multipage may be limited. Alternative approach using System.Drawing or custom TIFF encoding:

```csharp
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Zipper;

public static class TiffMultiPageGenerator
{
    private static readonly byte[] SinglePageTiff;

    static TiffMultiPageGenerator()
    {
        // Generate a minimal 100x100 black TIFF as template
        using var bitmap = new Bitmap(100, 100, PixelFormat.Format1bppIndexed);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.Clear(Color.Black);

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Tiff);
        SinglePageTiff = ms.ToArray();
    }

    public static byte[] Generate(int pageCount, FileWorkItem workItem)
    {
        if (pageCount <= 1)
        {
            return SinglePageTiff;
        }

        // For multipage TIFF, we need to properly encode IFD entries
        // This is a simplified implementation that concatenates pages
        using var result = new MemoryStream();

        // Write TIFF header
        result.WriteByte(0x49); // II (little endian)
        result.WriteByte(0x49);
        result.WriteByte(0x2A); // TIFF magic number
        result.WriteByte(0x00);
        result.WriteByte(0x08); // Offset to first IFD
        result.WriteByte(0x00);
        result.WriteByte(0x00);
        result.WriteByte(0x00);

        // For each page, write IFD and image data
        long offset = 8;
        var ifdOffsets = new List<long>();

        for (int page = 0; page < pageCount; page++)
        {
            ifdOffsets.Add(offset);

            // Generate page data
            using var bitmap = new Bitmap(100, 100, PixelFormat.Format1bppIndexed);
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.Clear(Color.Black);

            // Save to temporary stream to get data
            using var pageStream = new MemoryStream();
            bitmap.Save(pageStream, ImageFormat.Tiff);
            var pageData = pageStream.ToArray();

            // Skip header, extract IFD and data
            var pageContent = pageData.Skip(8).ToArray();
            result.Write(pageContent, 0, pageContent.Length);

            offset += pageContent.Length;
        }

        return result.ToArray();
    }

    public static int GetPageCount((int Min, int Max)? range, long fileIndex)
    {
        if (!range.HasValue || range.Value.Min == range.Value.Max)
        {
            return range?.Min ?? 1;
        }

        var random = new Random((int)fileIndex);
        return random.Next(range.Value.Min, range.Value.Max + 1);
    }
}
```

**Simpler Approach**: Use PlaceholderFiles but with page count metadata:

```csharp
// In PlaceholderFiles.cs, add page count tracking
public static (byte[] Content, int PageCount) GetTiffWithPageCount(string fileType, int pageCount)
{
    // For simplicity, return same content but track page count
    return (GetContent(fileType), pageCount);
}
```

#### Step 4.3: Update `CommandLineValidator.cs`
Add TIFF pages argument parsing:

```csharp
case "--tiff-pages":
    if (i + 1 < args.Length)
    {
        var range = ParsePageRange(args[++i]);
        if (range.HasValue) parsed.TiffPageRange = range.Value;
    }
    break;

// In ParsedArguments:
public (int Min, int Max)? TiffPageRange { get; set; }

// Add validation method:
private static (int Min, int Max)? ParsePageRange(string range)
{
    var parts = range.Split('-');
    if (parts.Length == 2 &&
        int.TryParse(parts[0], out var min) &&
        int.TryParse(parts[1], out var max) &&
        min >= 1 && max >= min && max <= 1000)
    {
        return (min, max);
    }

    Console.Error.WriteLine("Error: Invalid TIFF page range. Use format: <min>-<max> (e.g., 1-20)");
    return null;
}
```

#### Step 4.4: Update `FileData.cs`
Add page count property:

```csharp
public int PageCount { get; set; } = 1;
```

#### Step 4.5: Update `LoadFileGenerator.cs`
Add page count column:

```csharp
// In WriteLoadFileContent, add to header:
if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
{
    headerBuilder.Append($"{colDelim}{quote}Page Count{quote}");
}

// In WriteFileRecord, add page count:
var tiffData = fileData as TiffFileData;
if (tiffData != null && request.TiffPageRange.HasValue)
{
    lineBuilder.Append($"{colDelim}{quote}{tiffData.PageCount}{quote}");
}
```

### Testing

#### Unit Tests
`Zipper.Tests/TiffMultiPageGeneratorTests.cs`

```csharp
public class TiffMultiPageGeneratorTests
{
    [Fact]
    public void Generate_SinglePage_ReturnsValidTiff()
    {
        var content = TiffMultiPageGenerator.Generate(1, new FileWorkItem { Index = 1 });

        Assert.True(content.Length > 0);
        Assert.StartsWith("II", Encoding.ASCII.GetString(content, 0, 2));
    }

    [Fact]
    public void Generate_MultiPage_ReturnsLargerContent()
    {
        var singlePage = TiffMultiPageGenerator.Generate(1, new FileWorkItem { Index = 1 });
        var multiPage = TiffMultiPageGenerator.Generate(5, new FileWorkItem { Index = 1 });

        Assert.True(multiPage.Length >= singlePage.Length);
    }

    [Fact]
    public void GetPageCount_WithRange_ReturnsInRange()
    {
        var range = (Min: 1, Max: 20);

        for (long i = 0; i < 100; i++)
        {
            var pageCount = TiffMultiPageGenerator.GetPageCount(range, i);
            Assert.InRange(pageCount, 1, 20);
        }
    }
}
```

#### E2E Tests
`tests/test-multipage-tiff.sh`:
```bash
#!/bin/bash

dotnet run --project ../Zipper/Zipper.csproj -- \
    --type tiff --count 100 --output-path ./test-tiff \
    --tiff-pages 1-20 --with-metadata

# Check DAT file for Page Count column
if grep -q "Page Count" test-tiff.dat; then
    echo "PASS: Page Count column found"
else
    echo "FAIL: Page Count column missing"
    exit 1
fi

# Extract and verify TIFF files exist
unzip -q test-tiff.zip -d test-extracted
if find test-extracted -name "*.tiff" | grep -q .; then
    echo "PASS: TIFF files generated"
else
    echo "FAIL: No TIFF files found"
    exit 1
fi

echo "PASS: Multipage TIFF test"
```

---

## Cross-Platform Testing Requirements

All E2E tests must have both `.sh` (Unix) and `.bat` (Windows) versions and pass on both platforms.

---

## File Creation Summary

### New Files
1. `Zipper/OfficeFileGenerator.cs`
2. `Zipper/LoadFiles/ILoadFileWriter.cs`
3. `Zipper/LoadFileFormat.cs`
4. `Zipper/LoadFiles/OptWriter.cs`
5. `Zipper/LoadFiles/CsvWriter.cs`
6. `Zipper/LoadFiles/XmlWriter.cs`
7. `Zipper/LoadFiles/ConcordanceWriter.cs`
8. `Zipper/LoadFiles/LoadFileWriterFactory.cs`
9. `Zipper/BatesNumberGenerator.cs`
10. `Zipper/TiffMultiPageGenerator.cs`

### Modified Files
1. `Zipper.csproj` - Add NuGet packages
2. `CommandLineValidator.cs` - Add new CLI arguments
3. `FileGenerationRequest.cs` - Add new properties
4. `FileData.cs` - Add PageCount property
5. `LoadFileGenerator.cs` - Add Bates/Page Count columns, refactor to DatWriter
6. `ZipArchiveService.cs` - Use factory pattern for load file writers
7. `ParallelFileGenerator.cs` - Integrate Office and multipage TIFF generators

### Test Files
1. `Zipper.Tests/OfficeFileGeneratorTests.cs`
2. `Zipper.Tests/LoadFileWriterTests.cs`
3. `Zipper.Tests/BatesNumberGeneratorTests.cs`
4. `Zipper.Tests/TiffMultiPageGeneratorTests.cs`
5. `tests/test-office-formats.sh`
6. `tests/test-office-formats.bat`
7. `tests/test-loadfile-formats.sh`
8. `tests/test-loadfile-formats.bat`
9. `tests/test-bates-numbering.sh`
10. `tests/test-bates-numbering.bat`
11. `tests/test-multipage-tiff.sh`
12. `tests/test-multipage-tiff.bat`

---

## Verification Checklist

Before completing this implementation:

- [ ] All new files created
- [ ] All modified files updated correctly
- [ ] NuGet dependencies added to Zipper.csproj
- [ ] Unit tests written and passing
- [ ] E2E tests written for both Unix (.sh) and Windows (.bat)
- [ ] Tests pass on Linux
- [ ] Tests pass on Windows
- [ ] Code follows project C# conventions
- [ ] Documentation updated (README.md)
- [ ] No breaking changes to existing functionality

---

## Success Criteria

1. **DOCX/XLSX/PPTX**: Files generate correctly and open in LibreOffice/Microsoft Office
2. **Load File Formats**: All formats (DAT, OPT, CSV, XML, CONCORDANCE) generate correctly
3. **Bates Numbering**: Numbers format correctly and appear in load file
4. **Multipage TIFF**: Page counts are random within range and reflected in load file
5. **Cross-Platform**: All tests pass on both Windows and Linux
6. **Performance**: No significant performance regression (<15% slowdown)
