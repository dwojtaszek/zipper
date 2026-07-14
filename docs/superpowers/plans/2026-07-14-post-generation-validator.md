# Post-Generation Validator Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate duplicated post-generation validation orchestration from three mode adapters into a single `PostGenerationValidator`, add multi-load-file support, and fill test coverage gaps.

**Architecture:** A new `PostGenerationValidator` class orchestrates all post-generation validation. Mode adapters construct a `ValidationContext` and call the validator instead of duplicating inline validation logic. `ProductionSetPostValidator` findings are merged into the unified `ValidationResult`.

**Tech Stack:** C#14, .NET10, xUnit

## Global Constraints

- C# 14 (net10.0), file-scoped namespaces, nullable reference types
- Warnings as Errors enabled
- Follow existing test naming: `{Subject}Tests` class, `{Method}_{Scenario}_{Expected}` methods
- Run `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj` after every change
- No copyright headers

---

### Task 1: Create `ValidationContext` record

**Files:**
- Create: `src/Validation/ValidationContext.cs`
- Test: `src/Zipper.Tests/PostGenerationValidatorTests.cs` (extend existing)

**Interfaces:**
- Produces: `ValidationContext` record consumed by `PostGenerationValidator` (Task 3)

- [ ] **Step 1: Write the failing test**

Add to `PostGenerationValidatorTests.cs`:

```csharp
[Fact]
public void ValidationContext_AllProperties_SetAndGet()
{
    var request = new FileGenerationRequest();
    var ctx = new ValidationContext
    {
        ArchiveFilePath = "/tmp/test.zip",
        LoadFiles = new Dictionary<string, string> { ["dat"] = "/tmp/test.dat" },
        ArchiveEntryPaths = new[] { "folder/file.pdf" },
        Request = request,
        SkipEolValidation = true,
        IsChaosMode = false,
    };

    Assert.Equal("/tmp/test.zip", ctx.ArchiveFilePath);
    Assert.Equal("/tmp/test.dat", ctx.LoadFiles["dat"]);
    Assert.True(ctx.SkipEolValidation);
    Assert.False(ctx.IsChaosMode);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "ValidationContext_AllProperties_SetAndGet"`
Expected: FAIL — `ValidationContext` not found

- [ ] **Step 3: Write implementation**

Create `src/Validation/ValidationContext.cs`:

```csharp
namespace Zipper.Validation;

public sealed record ValidationContext
{
    public string? ArchiveFilePath { get; init; }

    public string? ProductionSetPath { get; init; }

    public IReadOnlyDictionary<string, string> LoadFiles { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string>? ArchiveEntryPaths { get; init; }

    public FileGenerationRequest Request { get; init; } = new();

    public bool SkipEolValidation { get; init; }

    public bool IsChaosMode { get; init; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "ValidationContext_AllProperties_SetAndGet"`
Expected: PASS

- [ ] **Step 5: Run full test suite**

Run: `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`
Expected: all PASS

- [ ] **Step 6: Commit**

```bash
git add src/Validation/ValidationContext.cs src/Zipper.Tests/PostGenerationValidatorTests.cs
git commit -m "feat(validation): add ValidationContext record for #568"
```

---

### Task 2: Extend `FileGenerationResult` with `LoadFilePaths`

**Files:**
- Modify: `src/FileGenerationRequest.cs:48-63`
- Test: `src/Zipper.Tests/PostGenerationValidatorTests.cs` (extend existing)

**Interfaces:**
- Produces: `FileGenerationResult.LoadFilePaths` property used by `PostGenerationValidator` (Task 3)

- [ ] **Step 1: Write the failing test**

Add to `PostGenerationValidatorTests.cs`:

```csharp
[Fact]
public void FileGenerationResult_LoadFilePaths_DefaultsEmpty()
{
    var result = new FileGenerationResult();

    Assert.NotNull(result.LoadFilePaths);
    Assert.Empty(result.LoadFilePaths);
}

[Fact]
public void FileGenerationResult_LoadFilePaths_PopulatesCorrectly()
{
    var paths = new Dictionary<string, string>
    {
        ["dat"] = "/tmp/output.dat",
        ["opt"] = "/tmp/output.opt",
    };
    var result = new FileGenerationResult { LoadFilePaths = paths };

    Assert.Equal(2, result.LoadFilePaths.Count);
    Assert.Equal("/tmp/output.dat", result.LoadFilePaths["dat"]);
    Assert.Equal("/tmp/output.opt", result.LoadFilePaths["opt"]);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FileGenerationResult_LoadFilePaths"`
Expected: FAIL — `LoadFilePaths` not found

- [ ] **Step 3: Write implementation**

In `src/FileGenerationRequest.cs`, add to `FileGenerationResult` class after `LoadFilePath`:

```csharp
public IReadOnlyDictionary<string, string> LoadFilePaths { get; set; }
    = new Dictionary<string, string>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FileGenerationResult_LoadFilePaths"`
Expected: PASS

- [ ] **Step 5: Run full test suite**

Run: `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`
Expected: all PASS

- [ ] **Step 6: Commit**

```bash
git add src/FileGenerationRequest.cs src/Zipper.Tests/PostGenerationValidatorTests.cs
git commit -m "feat(validation): add LoadFilePaths to FileGenerationResult for #568"
```

---

### Task 3: Create `PostGenerationValidator` orchestrator

**Files:**
- Create: `src/Validation/PostGenerationValidator.cs`
- Test: `src/Zipper.Tests/PostGenerationValidatorTests.cs` (extend existing)

**Interfaces:**
- Consumes: `ValidationContext` (Task 1), `ValidatorRunner`, `ProductionSetPostValidator`
- Produces: `ValidationResult` consumed by mode adapters (Tasks 4-6)

**Format name mapping** (used by this task and mode adapters):

| `LoadFileFormat` | Format name string | Extension |
|---|---|---|
| `Dat` | `"dat"` | `.dat` |
| `Opt` | `"opt"` | `.opt` |
| `Csv` | `"csv"` | `.csv` |
| `Concordance` | `"concordance"` | `.dat` |
| `EdrmXml` | skip | `.xml` |

- [ ] **Step 1: Write the failing test — valid CSV from disk**

Add to `PostGenerationValidatorTests.cs`:

```csharp
[Fact]
public void Validate_ValidCsvFromDisk_Passes()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        File.WriteAllText(tempFile, "a,b,c\nd,e,f\n");
        var ctx = new ValidationContext
        {
            LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
            Request = new FileGenerationRequest(),
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.False(result.HasErrors);
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_ValidCsvFromDisk_Passes"`
Expected: FAIL — `PostGenerationValidator` not found

- [ ] **Step 3: Write implementation — skeleton**

Create `src/Validation/PostGenerationValidator.cs`:

```csharp
using System.IO.Compression;

namespace Zipper.Validation;

public sealed class PostGenerationValidator
{
    public ValidationResult Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = new ValidationResult();

        if (context.IsChaosMode)
            return result;

        if (context.ProductionSetPath is not null)
        {
            ValidateProductionSet(context, result);
            return result;
        }

        if (context.ArchiveFilePath is not null)
        {
            ValidateArchive(context, result);
        }
        else
        {
            ValidateDiskFiles(context, result);
        }

        return result;
    }

    private static void ValidateDiskFiles(ValidationContext context, ValidationResult result)
    {
        var runner = new ValidatorRunner();
        foreach (var (formatName, filePath) in context.LoadFiles)
        {
            if (formatName == "edrmxml" || !File.Exists(filePath))
                continue;

            var eol = context.SkipEolValidation ? null : GetExpectedEol(context.Request);
            var vr = runner.ValidateLoadFile(
                filePath,
                formatName,
                null,
                eol,
                bates: context.Request.Bates,
                encoding: EncodingHelper.GetEncodingOrDefault(context.Request.LoadFile.Encoding),
                columnDelimiter: context.Request.Delimiters.GetColumnChar(),
                quoteDelimiter: context.Request.Delimiters.GetQuoteChar());
            result.AddRange(vr.Findings);
        }
    }

    private static void ValidateArchive(ValidationContext context, ValidationResult result)
    {
        using var archive = ZipFile.OpenRead(context.ArchiveFilePath!);
        var entryPaths = context.ArchiveEntryPaths
            ?? archive.Entries.Select(e => e.FullName).ToArray();
        var runner = new ValidatorRunner();

        foreach (var (formatName, _) in context.LoadFiles)
        {
            if (formatName == "edrmxml")
                continue;

            var extension = GetExtensionForFormat(formatName);
            var baseName = Path.GetFileNameWithoutExtension(context.LoadFiles[formatName]);
            var fileName = baseName + extension;
            var entry = archive.GetEntry(fileName);
            if (entry is null)
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "MissingLoadFile",
                    $"Generated load file '{fileName}' is missing from the Archive.",
                    context.ArchiveFilePath));
                continue;
            }

            using var reader = new StreamReader(
                entry.Open(),
                EncodingHelper.GetEncodingOrDefault(context.Request.LoadFile.Encoding),
                detectEncodingFromByteOrderMarks: true);
            var vr = runner.ValidateLoadFile(
                reader,
                entry.FullName,
                formatName,
                null,
                entryPaths,
                context.Request.Bates,
                context.Request.Delimiters.GetColumnChar(),
                context.Request.Delimiters.GetQuoteChar());
            result.AddRange(vr.Findings);
        }
    }

    private static void ValidateProductionSet(ValidationContext context, ValidationResult result)
    {
        var report = ProductionSetPostValidator.Validate(context.ProductionSetPath!, context.Request);
        foreach (var finding in report.Findings)
        {
            result.Add(new ValidationFinding(
                string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase)
                    ? ValidationSeverity.Error
                    : ValidationSeverity.Warning,
                finding.Code,
                finding.Message,
                finding.Path,
                finding.Line));
        }
    }

    private static string? GetExpectedEol(FileGenerationRequest request)
    {
        return request.Delimiters.EndOfLine?.ToUpperInvariant() switch
        {
            "CRLF" => "\r\n",
            "LF" => "\n",
            "CR" => "\r",
            _ => null
        };
    }

    private static string GetExtensionForFormat(string formatName) => formatName switch
    {
        "opt" => ".opt",
        "csv" => ".csv",
        "concordance" => ".dat",
        _ => ".dat",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_ValidCsvFromDisk_Passes"`
Expected: PASS

- [ ] **Step 5: Add test — column count mismatch from disk**

Add to `PostGenerationValidatorTests.cs`:

```csharp
[Fact]
public void Validate_ColumnCountMismatch_ReportsError()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        File.WriteAllText(tempFile, "a,b,c\nd,e\n");
        var ctx = new ValidationContext
        {
            LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
            Request = new FileGenerationRequest(),
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, f => f.Category == "ColumnCount");
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_ColumnCountMismatch_ReportsError"`
Expected: PASS

- [ ] **Step 7: Add test — chaos mode skips validation**

Add to `PostGenerationValidatorTests.cs`:

```csharp
[Fact]
public void Validate_ChaosMode_ReturnsEmptyResult()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        File.WriteAllText(tempFile, "a,b\n");
        var ctx = new ValidationContext
        {
            LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
            Request = new FileGenerationRequest(),
            IsChaosMode = true,
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Findings);
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_ChaosMode_ReturnsEmptyResult"`
Expected: PASS

- [ ] **Step 9: Add test — production set delegates to ProductionSetPostValidator**

Add to `PostGenerationValidatorTests.cs`:

```csharp
[Fact]
public void Validate_ProductionSet_DelegatesAndMergesFindings()
{
    var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"validator_test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
        // Create minimal production set structure
        var dataDir = Path.Combine(tempDir, "DATA");
        Directory.CreateDirectory(dataDir);
        var nativesDir = Path.Combine(tempDir, "NATIVES");
        Directory.CreateDirectory(nativesDir);

        // Create a minimal valid DAT file
        var datPath = Path.Combine(dataDir, "loadfile.dat");
        File.WriteAllText(datPath, "DOCID\u0014BATES_NUMBER\u0014NATIVE_PATH\nDOC001\u0014TEST00000001\u0014NATIVES/file1.pdf\n");

        // Create a minimal valid OPT file
        var optPath = Path.Combine(dataDir, "loadfile.opt");
        File.WriteAllText(optPath, "TEST00000001,1,NATIVES/file1.pdf,,,1,1\n");

        // Create the referenced native file
        File.WriteAllBytes(Path.Combine(nativesDir, "file1.pdf"), [0x25, 0x50, 0x44, 0x46]);

        var request = new FileGenerationRequest
        {
            Bates = new Config.BatesNumberConfig { Prefix = "TEST", Start = 1, Digits = 8 },
        };
        var ctx = new ValidationContext
        {
            ProductionSetPath = tempDir,
            Request = request,
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.False(result.HasErrors);
    }
    finally
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }
}
```

- [ ] **Step 10: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_ProductionSet_DelegatesAndMergesFindings"`
Expected: PASS

- [ ] **Step 11: Run full test suite**

Run: `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`
Expected: all PASS

- [ ] **Step 12: Commit**

```bash
git add src/Validation/PostGenerationValidator.cs src/Zipper.Tests/PostGenerationValidatorTests.cs
git commit -m "feat(validation): add PostGenerationValidator orchestrator for #568"
```

---

### Task 4: Update `StandardMode` to use `PostGenerationValidator`

**Files:**
- Modify: `src/StandardMode.cs:88-134` (replace `ValidateGeneratedLoadFile` method)

**Interfaces:**
- Consumes: `PostGenerationValidator`, `ValidationContext` (Tasks 1, 3)
- Produces: Nothing new

- [ ] **Step 1: Read current StandardMode validation code**

Read `src/StandardMode.cs` lines 88-156 to understand the current `ValidateGeneratedLoadFile` method and helper methods.

- [ ] **Step 2: Replace `ValidateGeneratedLoadFile` with `PostGenerationValidator` call**

Replace the `ValidateGeneratedLoadFile` call at line 90 and the entire private method (lines 94-134) plus helpers (lines 136-155) with:

In `RunAsync`, replace:
```csharp
if (!string.IsNullOrEmpty(result.LoadFilePath))
{
    ValidateGeneratedLoadFile(result, request);
}
```
with:
```csharp
if (!string.IsNullOrEmpty(result.LoadFilePath))
{
    var context = new ValidationContext
    {
        ArchiveFilePath = result.ZipFilePath,
        LoadFiles = result.LoadFilePaths.Count > 0
            ? result.LoadFilePaths
            : BuildDefaultLoadFiles(result, request),
        Request = request,
        SkipEolValidation = true, // ponytail: StandardMode uses Environment.NewLine regardless of --eol config
    };
    var validator = new PostGenerationValidator();
    var vr = validator.Validate(context);
    if (vr.HasErrors || vr.HasWarnings)
    {
        Console.Error.WriteLine(vr.GetSummary());
        if (vr.HasErrors)
            throw new InvalidOperationException("Post-generation validation failed.");
    }
}
```

Add private helper:
```csharp
private static IReadOnlyDictionary<string, string> BuildDefaultLoadFiles(FileGenerationResult result, FileGenerationRequest request)
{
    var baseName = Path.GetFileNameWithoutExtension(result.LoadFilePath);
    var directory = Path.GetDirectoryName(result.LoadFilePath) ?? string.Empty;
    var formats = request.LoadFile.Formats.Count > 0 ? request.LoadFile.Formats : new[] { LoadFileFormat.Dat };
    var paths = new Dictionary<string, string>();
    foreach (var format in formats)
    {
        var name = format switch
        {
            LoadFileFormat.Opt => "opt",
            LoadFileFormat.Csv => "csv",
            LoadFileFormat.Concordance => "concordance",
            _ => "dat",
        };
        var ext = format switch
        {
            LoadFileFormat.Opt => ".opt",
            LoadFileFormat.Csv => ".csv",
            LoadFileFormat.EdrmXml => ".xml",
            _ => ".dat",
        };
        if (format == LoadFileFormat.EdrmXml)
            continue;
        paths[name] = Path.Combine(directory, baseName + ext);
    }
    return paths;
}
```

Remove the old `ValidateGeneratedLoadFile`, `GetDistinctFormats`, `GetFormatName`, and `GetExtension` private methods (lines 94-155).

- [ ] **Step 3: Run full test suite**

Run: `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`
Expected: all PASS

- [ ] **Step 4: Commit**

```bash
git add src/StandardMode.cs
git commit -m "refactor(validation): use PostGenerationValidator in StandardMode for #568"
```

---

### Task 5: Update `LoadFileOnlyMode` to use `PostGenerationValidator`

**Files:**
- Modify: `src/LoadFileOnlyMode.cs:63-109` (replace `ValidateGeneratedLoadFile` method)

**Interfaces:**
- Consumes: `PostGenerationValidator`, `ValidationContext` (Tasks 1, 3)
- Produces: Nothing new

- [ ] **Step 1: Read current LoadFileOnlyMode validation code**

Read `src/LoadFileOnlyMode.cs` lines 63-109 to understand the current validation logic.

- [ ] **Step 2: Replace `ValidateGeneratedLoadFile` with `PostGenerationValidator` call**

Replace the `ValidateGeneratedLoadFile` call at line 65 and the entire private method (lines 69-109) with:

In `RunAsync`, replace:
```csharp
if (!string.IsNullOrEmpty(result.LoadFilePath) && !request.Chaos.ChaosMode)
{
    ValidateGeneratedLoadFile(result.LoadFilePath, request);
}
```
with:
```csharp
if (!string.IsNullOrEmpty(result.LoadFilePath) && !request.Chaos.ChaosMode)
{
    var formats = request.LoadFile.Formats.Count > 0 ? request.LoadFile.Formats : new[] { LoadFileFormat.Dat };
    var loadFiles = new Dictionary<string, string>();
    foreach (var format in formats)
    {
        var name = format switch
        {
            LoadFileFormat.Opt => "opt",
            LoadFileFormat.Csv => "csv",
            LoadFileFormat.Concordance => "concordance",
            _ => "dat",
        };
        var filePath = format == LoadFileFormat.Opt
            ? Path.ChangeExtension(result.LoadFilePath, ".opt")
            : result.LoadFilePath;
        if (File.Exists(filePath))
            loadFiles[name] = filePath;
    }

    var context = new ValidationContext
    {
        LoadFiles = loadFiles,
        Request = request,
    };
    var validator = new PostGenerationValidator();
    var vr = validator.Validate(context);
    if (vr.HasErrors || vr.HasWarnings)
    {
        Console.Error.WriteLine(vr.GetSummary());
        if (vr.HasErrors)
            throw new InvalidOperationException("Post-generation validation failed.");
    }
}
```

Remove the old `ValidateGeneratedLoadFile` private method (lines 69-109).

- [ ] **Step 3: Run full test suite**

Run: `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`
Expected: all PASS

- [ ] **Step 4: Commit**

```bash
git add src/LoadFileOnlyMode.cs
git commit -m "refactor(validation): use PostGenerationValidator in LoadFileOnlyMode for #568"
```

---

### Task 6: Update `ProductionSetMode` to use `PostGenerationValidator`

**Files:**
- Modify: `src/ProductionSetMode.cs:57-92` (replace inline validation)

**Interfaces:**
- Consumes: `PostGenerationValidator`, `ValidationContext` (Tasks 1, 3)
- Produces: Nothing new

- [ ] **Step 1: Read current ProductionSetMode validation code**

Read `src/ProductionSetMode.cs` lines 49-92 to understand the current validation logic.

- [ ] **Step 2: Replace inline validation with `PostGenerationValidator` call**

Replace lines 49-92 (the EOL parsing and two validation blocks) with:

```csharp
var loadFiles = new Dictionary<string, string>();
if (!string.IsNullOrEmpty(result.DatFilePath) && File.Exists(result.DatFilePath))
    loadFiles["dat"] = result.DatFilePath;
if (!string.IsNullOrEmpty(result.OptFilePath) && File.Exists(result.OptFilePath))
    loadFiles["opt"] = result.OptFilePath;

if (loadFiles.Count > 0)
{
    var context = new ValidationContext
    {
        ProductionSetPath = Path.GetDirectoryName(result.DatFilePath),
        LoadFiles = loadFiles,
        Request = request,
    };
    var validator = new PostGenerationValidator();
    var vr = validator.Validate(context);
    if (vr.HasErrors || vr.HasWarnings)
    {
        Console.Error.WriteLine(vr.GetSummary());
        if (vr.HasErrors)
            throw new InvalidOperationException("Post-generation validation failed.");
    }
}
```

- [ ] **Step 3: Run full test suite**

Run: `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`
Expected: all PASS

- [ ] **Step 4: Commit**

```bash
git add src/ProductionSetMode.cs
git commit -m "refactor(validation): use PostGenerationValidator in ProductionSetMode for #568"
```

---

### Task 7: Add missing test scenarios

**Files:**
- Modify: `src/Zipper.Tests/PostGenerationValidatorTests.cs`

**Interfaces:**
- Consumes: `PostGenerationValidator`, `ValidationContext` (Tasks 1, 3)

- [ ] **Step 1: Add test — valid Standard Mode Archive passes**

```csharp
[Fact]
public void Validate_ValidArchiveFromDisk_Passes()
{
    var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"archive_test_{Guid.NewGuid():N}");
    var zipPath = tempDir + ".zip";
    try
    {
        Directory.CreateDirectory(tempDir);
        var dataDir = Path.Combine(tempDir, "DATA");
        Directory.CreateDirectory(dataDir);

        // Create a valid DAT file
        File.WriteAllText(Path.Combine(dataDir, "loadfile.dat"),
            "DOCID\u0014FILEPATH\nDOC001\u0014folder_001/file_00000001.pdf\nDOC002\u0014folder_001/file_00000002.pdf\n");

        // Create referenced files
        var nativesDir = Path.Combine(tempDir, "folder_001");
        Directory.CreateDirectory(nativesDir);
        File.WriteAllBytes(Path.Combine(nativesDir, "file_00000001.pdf"), [0x25, 0x50, 0x44, 0x46]);
        File.WriteAllBytes(Path.Combine(nativesDir, "file_00000002.pdf"), [0x25, 0x50, 0x44, 0x46]);

        // Create ZIP archive
        ZipFile.CreateFromDirectory(tempDir, zipPath);

        var entryPaths = new[]
        {
            "folder_001/file_00000001.pdf",
            "folder_001/file_00000002.pdf",
        };
        var loadFiles = new Dictionary<string, string> { ["dat"] = Path.Combine(dataDir, "loadfile.dat") };

        var ctx = new ValidationContext
        {
            ArchiveFilePath = zipPath,
            LoadFiles = loadFiles,
            ArchiveEntryPaths = entryPaths,
            Request = new FileGenerationRequest(),
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.False(result.HasErrors);
    }
    finally
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
        if (File.Exists(zipPath))
            File.Delete(zipPath);
    }
}
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_ValidArchiveFromDisk_Passes"`
Expected: PASS

- [ ] **Step 3: Add test — Concordance quote-wrapped records validate**

```csharp
[Fact]
public void Validate_ConcordanceQuoteWrappedRecords_NoFalsePositive()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        // Concordance format uses \x14 column delim, \xfe quote delim
        // Quote-wrapped field containing column delimiter should not cause column count error
        var line1 = "\xfeDOCID\xfe\u0014\xfeFILEPATH\xfe";
        var line2 = "\xfeDOC001\xfe\u0014\xfevalue with \xfe\u0014\xfe inside\xfe";
        File.WriteAllText(tempFile, line1 + "\n" + line2 + "\n");

        var ctx = new ValidationContext
        {
            LoadFiles = new Dictionary<string, string> { ["concordance"] = tempFile },
            Request = new FileGenerationRequest(),
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.DoesNotContain(result.Findings, f => f.Category == "ColumnCount");
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_ConcordanceQuoteWrappedRecords_NoFalsePositive"`
Expected: PASS

- [ ] **Step 5: Add test — large-file streaming**

```csharp
[Fact]
public void Validate_LargeFile_NoOomCorrectFindings()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("a,b,c");
        for (int i = 0; i < 15000; i++)
        {
            sb.AppendLine($"d{i},e{i},f{i}");
        }
        File.WriteAllText(tempFile, sb.ToString());

        var ctx = new ValidationContext
        {
            LoadFiles = new Dictionary<string, string> { ["csv"] = tempFile },
            Request = new FileGenerationRequest(),
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Findings);
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_LargeFile_NoOomCorrectFindings"`
Expected: PASS

- [ ] **Step 7: Add test — multi-format validation**

```csharp
[Fact]
public void Validate_MultipleFormats_ValidatesAll()
{
    var tempDat = Path.GetTempFileName();
    var tempOpt = Path.GetTempFileName();
    try
    {
        File.WriteAllText(tempDat, "a,b,c\nd,e,f\n");
        File.WriteAllText(tempOpt, "a,b,c,d,e,f,g\n");

        var ctx = new ValidationContext
        {
            LoadFiles = new Dictionary<string, string>
            {
                ["dat"] = tempDat,
                ["opt"] = tempOpt,
            },
            Request = new FileGenerationRequest(),
        };

        var validator = new PostGenerationValidator();
        var result = validator.Validate(ctx);

        Assert.False(result.HasErrors);
    }
    finally
    {
        File.Delete(tempDat);
        File.Delete(tempOpt);
    }
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Validate_MultipleFormats_ValidatesAll"`
Expected: PASS

- [ ] **Step 9: Run full test suite**

Run: `dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj`
Expected: all PASS

- [ ] **Step 10: Commit**

```bash
git add src/Zipper.Tests/PostGenerationValidatorTests.cs
git commit -m "test(validation): add missing test scenarios for #568"
```
