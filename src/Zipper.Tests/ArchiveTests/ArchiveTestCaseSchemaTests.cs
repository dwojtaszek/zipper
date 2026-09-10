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

    private const string FrozenValidFixtureId = "atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950";

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

        return errors;
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static bool IsBoundedHex(string value, int maxBytes) =>
        value.Length % 2 == 0
        && value.Length <= maxBytes * 2
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
        var changed = ComputeFixtureId("1", "valid-empty", 1, 1, 43, "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85");

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
