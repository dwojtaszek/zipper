using System.Text.Json;
using Xunit;

namespace Zipper.Tests;

/// <summary>
/// Meta-validation for the Archive Test Fixture contract (docs/archive-test-suites.md, planned per #834):
/// the draft-07 schema declares the frozen contract, and these semantic checks cover equalities
/// draft-07 cannot express (basename == Fixture ID, unique ordinals, hex bounds, identity recomputation).
/// </summary>
public class ArchiveTestCaseSchemaTests
{

    private const string FrozenValidFixtureId = "atc-4d275f1fe266174e43c71d2cbe42084da23ce42369a17af7b84a9d165b6c489f";

    private static readonly string[] RequiredFields =
    [
        "schemaVersion", "generatorContractVersion", "generatorVersion", "fixtureId", "caseKey",
        "caseRevision", "expectationRevision", "seed", "classification", "archive", "entries",
        "mutations", "expectations", "limits"
    ];

    private static string FixturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException($"tests/fixtures not found above {AppContext.BaseDirectory}")
            : Path.Combine(dir.FullName, "tests", "fixtures");
    }

    private static JsonDocument LoadJson(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesRoot(), relativePath)));

    private static List<string> ValidateSemantics(JsonElement doc)
    {
        var errors = new List<string>();
        if (doc.ValueKind != JsonValueKind.Object)
        {
            errors.Add("root must be a JSON object");
            return errors;
        }

        foreach (var field in RequiredFields)
        {
            if (!doc.TryGetProperty(field, out _))
            {
                errors.Add($"missing required field '{field}'");
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var fixtureId = GetString(doc, "fixtureId");
        if (fixtureId.Length != 68 || !fixtureId.StartsWith("atc-", StringComparison.Ordinal) || !IsLowercaseHex(fixtureId[4..]))
        {
            errors.Add("fixtureId must be atc- plus 64 lowercase hex characters");
        }

        var classification = GetString(doc, "classification");
        if (classification is not ("valid" or "malformed" or "policy-sensitive"))
        {
            errors.Add($"unknown classification '{classification}'");
        }

        if (GetString(doc, "caseKey").Length == 0)
        {
            errors.Add("caseKey must not be empty");
        }

        var archive = doc.GetProperty("archive");
        if (!archive.TryGetProperty("fileName", out var fileNameProp))
        {
            errors.Add("archive.fileName missing");
        }
        else
        {
            var fileName = fileNameProp.GetString() ?? string.Empty;
            if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains('\0'))
            {
                errors.Add("archive.fileName must be a bare basename without path separators");
            }

            if (fileName != fixtureId + ".zip")
            {
                errors.Add("archive.fileName must equal fixtureId + \".zip\"");
            }
        }

        if (!archive.TryGetProperty("sha256", out var shaProp) || !IsLowercaseSha256(shaProp.GetString() ?? string.Empty))
        {
            errors.Add("archive.sha256 (final hash) is missing or not 64 lowercase hex characters");
        }

        var seenOrdinals = new HashSet<int>();
        foreach (var entry in doc.GetProperty("entries").EnumerateArray())
        {
            var ordinal = entry.GetProperty("ordinal").GetInt32();
            if (!seenOrdinals.Add(ordinal))
            {
                errors.Add($"duplicate entry ordinal {ordinal}");
            }

            if (entry.GetProperty("kind").GetString() is not ("file" or "directory"))
            {
                errors.Add($"entry {ordinal}: kind must be file or directory");
            }

            foreach (var hexField in new[] { "localNameRaw", "centralNameRaw" })
            {
                if (entry.TryGetProperty(hexField, out var hex) && !IsBoundedHex(hex.GetString() ?? string.Empty, int.MaxValue))
                {
                    errors.Add($"entry {ordinal}: {hexField} must be lowercase hex with even length");
                }
            }

            if (!entry.TryGetProperty("localHeaderMethod", out var lhm) || lhm.GetInt32() is < 0 or > 65535)
            {
                errors.Add($"entry {ordinal}: localHeaderMethod must be between 0 and 65535");
            }

            if (!entry.TryGetProperty("centralDirectoryMethod", out var cdm) || cdm.GetInt32() is < 0 or > 65535)
            {
                errors.Add($"entry {ordinal}: centralDirectoryMethod must be between 0 and 65535");
            }

            var hasCodec = entry.TryGetProperty("payloadCodec", out var codecProp);
            var isFile = entry.GetProperty("kind").GetString() == "file";
            if (isFile && !hasCodec)
            {
                errors.Add($"entry {ordinal}: file entry must declare a payloadCodec");
            }
            else if (!isFile && hasCodec)
            {
                errors.Add($"entry {ordinal}: directory entry must not declare a payloadCodec");
            }
            else if (hasCodec && codecProp.GetString() is not ("stored" or "deflate" or "deflate64" or "bzip2" or "unknown"))
            {
                errors.Add($"entry {ordinal}: unknown payloadCodec '{codecProp.GetString()}'");
            }
        }

        foreach (var mutation in doc.GetProperty("mutations").EnumerateArray())
        {
            var code = GetString(mutation, "code");
            if (code.Length == 0)
            {
                errors.Add("mutation code must not be empty");
            }

            if (GetString(mutation, "offsetBasis") != "before-mutation")
            {
                errors.Add($"mutation '{code}': offsetBasis must be before-mutation");
            }

            foreach (var hexField in new[] { "beforeHex", "afterHex" })
            {
                if (mutation.TryGetProperty(hexField, out var hex))
                {
                    var value = hex.GetString() ?? string.Empty;
                    if (!IsBoundedHex(value, 256))
                    {
                        errors.Add($"mutation '{code}': {hexField} must be lowercase hex with even length and at most 256 bytes (512 hex characters)");
                    }
                }
            }
        }

        var expectations = doc.GetProperty("expectations").EnumerateArray().ToList();
        if (expectations.Count == 0)
        {
            errors.Add("expectations must contain at least one operation record");
        }

        foreach (var expectation in expectations)
        {
            var operation = GetString(expectation, "operation");
            if (operation is not ("list" or "read-entry" or "integrity-check" or "extract"))
            {
                errors.Add($"unknown operation '{operation}'");
            }

            if (GetString(expectation, "profile").Length == 0)
            {
                errors.Add($"operation '{operation}': profile must not be empty");
            }

            if (expectation.TryGetProperty("allowedOutcomes", out var outcomes)
                && outcomes.GetArrayLength() == 0)
            {
                errors.Add($"operation '{operation}': allowedOutcomes must not be empty");
            }
        }

        if (doc.TryGetProperty("limits", out var limits))
        {
            if (limits.TryGetProperty("entryCount", out var entryCount) && entryCount.GetInt32() > 1_000)
            {
                errors.Add("limits.entryCount exceeds the 1,000-entry budget");
            }

            if (limits.TryGetProperty("expandedBytesBudget", out var expanded) && expanded.GetInt64() > 32 * 1024 * 1024)
            {
                errors.Add("limits.expandedBytesBudget exceeds the 32 MiB budget");
            }

            if (limits.TryGetProperty("jsonBytesBudget", out var jsonBudget) && jsonBudget.GetInt64() > 1024 * 1024)
            {
                errors.Add("limits.jsonBytesBudget exceeds the 1 MiB budget");
            }

            if (limits.TryGetProperty("deadlineSeconds", out var deadline) && deadline.GetInt32() > 10)
            {
                errors.Add("limits.deadlineSeconds exceeds the 10-second budget");
            }
        }

        if (archive.TryGetProperty("physicalSize", out var physSize) && physSize.GetInt64() > 16 * 1024 * 1024)
        {
            errors.Add("archive.physicalSize exceeds the 16 MiB budget");
        }

        return errors;
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static bool IsBoundedHex(string value, int maxBytes) =>
        value.Length % 2 == 0
        && value.Length <= (long)maxBytes * 2
        && IsLowercaseHex(value);

    private static bool IsLowercaseHex(string value) =>
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsLowercaseSha256(string value) =>
        value.Length == 64 && IsLowercaseHex(value);

    internal static string ComputeFixtureId(
        string generatorContractVersion, string caseKey, int caseRevision, int expectationRevision, int seed, string archiveSha256) =>
        Zipper.ArchiveTests.ArchiveTestIdentity.ComputeFixtureId(
            generatorContractVersion, caseKey, caseRevision, expectationRevision, seed, archiveSha256);

    [Fact]
    public void Load_SchemaFile_DeclaresDraft07Contract()
    {
        using var schema = LoadJson("archive-test-case.schema.json");
        var root = schema.RootElement;

        Assert.Equal("http://json-schema.org/draft-07/schema#", GetString(root, "$schema"));
        Assert.True(root.TryGetProperty("definitions", out _), "draft-07 uses definitions, not $defs");
        Assert.False(root.TryGetProperty("$defs", out _), "draft-07 uses definitions, not $defs");

        var declared = root.GetProperty("required").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Equal(RequiredFields.Order(), declared.Order());

        Assert.Equal(
            ["valid", "malformed", "policy-sensitive"],
            root.GetProperty("properties").GetProperty("classification").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));

        var mutationItem = root.GetProperty("properties").GetProperty("mutations").GetProperty("items");
        Assert.Equal(512, mutationItem.GetProperty("properties").GetProperty("beforeHex").GetProperty("maxLength").GetInt32());
        Assert.Equal(512, mutationItem.GetProperty("properties").GetProperty("afterHex").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Load_SchemaFile_DeclaresFixedReq213Maxima()
    {
        using var schema = LoadJson("archive-test-case.schema.json");
        var properties = schema.RootElement.GetProperty("properties");

        var archiveProps = properties.GetProperty("archive").GetProperty("properties");
        Assert.Equal(16 * 1024 * 1024, archiveProps.GetProperty("physicalSize").GetProperty("maximum").GetInt64());

        Assert.Equal(1_000, properties.GetProperty("entries").GetProperty("maxItems").GetInt32());

        var limitsProps = properties.GetProperty("limits").GetProperty("properties");
        Assert.Equal(1_000, limitsProps.GetProperty("entryCount").GetProperty("maximum").GetInt32());
        Assert.Equal(32 * 1024 * 1024, limitsProps.GetProperty("expandedBytesBudget").GetProperty("maximum").GetInt64());
        Assert.Equal(1024 * 1024, limitsProps.GetProperty("jsonBytesBudget").GetProperty("maximum").GetInt64());
        Assert.Equal(10, limitsProps.GetProperty("deadlineSeconds").GetProperty("maximum").GetInt32());
    }

    [Fact]
    public void Load_SchemaFile_DeclaresCompressionMethodProperties()
    {
        using var schema = LoadJson("archive-test-case.schema.json");
        var entryItem = schema.RootElement.GetProperty("properties").GetProperty("entries").GetProperty("items");
        var required = entryItem.GetProperty("required").EnumerateArray().Select(s => s.GetString()).ToList();

        Assert.Contains("localHeaderMethod", required);
        Assert.Contains("centralDirectoryMethod", required);

        var entryProps = entryItem.GetProperty("properties");
        var localMethod = entryProps.GetProperty("localHeaderMethod");
        Assert.Equal("integer", localMethod.GetProperty("type").GetString());
        Assert.Equal(0, localMethod.GetProperty("minimum").GetInt32());
        Assert.Equal(65535, localMethod.GetProperty("maximum").GetInt32());

        var centralMethod = entryProps.GetProperty("centralDirectoryMethod");
        Assert.Equal("integer", centralMethod.GetProperty("type").GetString());
        Assert.Equal(0, centralMethod.GetProperty("minimum").GetInt32());
        Assert.Equal(65535, centralMethod.GetProperty("maximum").GetInt32());

        var payloadCodec = entryProps.GetProperty("payloadCodec");
        Assert.Equal("string", payloadCodec.GetProperty("type").GetString());
        var allowedCodecs = payloadCodec.GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToList();
        Assert.Equal(["stored", "deflate", "deflate64", "bzip2", "unknown"], allowedCodecs);

        Assert.Equal("file", entryItem.GetProperty("if").GetProperty("properties").GetProperty("kind").GetProperty("const").GetString());
        Assert.Contains("payloadCodec", entryItem.GetProperty("then").GetProperty("required").EnumerateArray().Select(s => s.GetString()));
        Assert.Contains("payloadCodec", entryItem.GetProperty("else").GetProperty("not").GetProperty("required").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public void Validate_ValidEmptyExample_PassesSemanticChecks()
    {
        using var example = LoadJson(Path.Combine("archive-tests", "valid-empty.json"));

        Assert.Empty(ValidateSemantics(example.RootElement));
    }

    [Fact]
    public void FixtureIdentity_ValidEmptyExample_MatchesFrozenVector()
    {
        // Real bytes: the published pair's Archive bytes hash to the recorded final hash,
        // and the canonical descriptor reproduces the published Fixture ID.
        var archiveBytes = File.ReadAllBytes(Path.Combine(FixturesRoot(), "archive-tests", "valid-empty.zip"));
        var archiveSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archiveBytes));

        using var example = LoadJson(Path.Combine("archive-tests", "valid-empty.json"));
        var root = example.RootElement;

        Assert.Equal(archiveSha256, root.GetProperty("archive").GetProperty("sha256").GetString());
        Assert.Equal(FrozenValidFixtureId, root.GetProperty("fixtureId").GetString());

        var recomputed = ComputeFixtureId(
            root.GetProperty("generatorContractVersion").GetString()!,
            root.GetProperty("caseKey").GetString()!,
            root.GetProperty("caseRevision").GetInt32(),
            root.GetProperty("expectationRevision").GetInt32(),
            checked((int)root.GetProperty("seed").GetInt64()),
            archiveSha256);

        Assert.Equal(FrozenValidFixtureId, recomputed);
    }

    [Fact]
    public void FixtureIdentity_DifferentSeed_ProducesDifferentFixtureId()
    {
        var changed = ComputeFixtureId("1", "valid-empty", 1, 2, 43, "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85");

        Assert.NotEqual(FrozenValidFixtureId, changed);
    }

    [Theory]
    [InlineData("mismatched-filename.json", "archive.fileName must equal fixtureId")]
    [InlineData("unknown-classification.json", "unknown classification")]
    [InlineData("missing-final-hash.json", "archive.sha256 (final hash) is missing")]
    [InlineData("duplicate-ordinal.json", "duplicate entry ordinal 0")]
    [InlineData("path-separators-in-basename.json", "archive.fileName must be a bare basename")]
    [InlineData("unbounded-inline-hex.json", "at most 256 bytes")]
    public void Validate_InvalidExample_IsRejected(string invalidExample, string expectedError)
    {
        using var example = LoadJson(Path.Combine("archive-tests", "invalid", invalidExample));

        var errors = ValidateSemantics(example.RootElement);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }
}
