using System.Text.Json.Serialization;

namespace Zipper.ArchiveTests;

/// <summary>
/// Typed expectation contract for one Archive Test Fixture, frozen by
/// tests/fixtures/archive-test-case.schema.json (REQ-217). Property names are
/// explicit so later record renames cannot change the on-disk JSON.
/// </summary>
internal sealed record ArchiveTestArchive(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("physicalSize")] long PhysicalSize,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record ArchiveTestEntry(
    [property: JsonPropertyName("ordinal")] int Ordinal,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("localNameRaw")] string LocalNameRaw,
    [property: JsonPropertyName("centralNameRaw")] string CentralNameRaw,
    [property: JsonPropertyName("readableName")] string ReadableName,
    [property: JsonPropertyName("contentSha256")] string? ContentSha256,
    [property: JsonPropertyName("contentSize")] long? ContentSize,
    [property: JsonPropertyName("localHeaderOffset")] long? LocalHeaderOffset,
    [property: JsonPropertyName("dataOffset")] long? DataOffset,
    [property: JsonPropertyName("centralDirectoryOffset")] long? CentralDirectoryOffset);

internal sealed record ArchiveTestMutation(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("structure")] string Structure,
    [property: JsonPropertyName("offsetBasis")] string OffsetBasis,
    [property: JsonPropertyName("offset")] long Offset,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("ordinal")] int? Ordinal,
    [property: JsonPropertyName("deletedLength")] long? DeletedLength,
    [property: JsonPropertyName("insertedLength")] long? InsertedLength,
    [property: JsonPropertyName("beforeSize")] long? BeforeSize,
    [property: JsonPropertyName("afterSize")] long? AfterSize,
    [property: JsonPropertyName("beforeSha256")] string? BeforeSha256,
    [property: JsonPropertyName("afterSha256")] string? AfterSha256,
    [property: JsonPropertyName("beforeHex")] string? BeforeHex,
    [property: JsonPropertyName("afterHex")] string? AfterHex,
    [property: JsonPropertyName("declaredValue")] string? DeclaredValue);

internal sealed record ArchiveTestExpectation(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("profile")] string Profile,
    [property: JsonPropertyName("allowedOutcomes")] IReadOnlyList<string> AllowedOutcomes,
    [property: JsonPropertyName("invariants")] IReadOnlyList<string> Invariants,
    [property: JsonPropertyName("platform")] string? Platform,
    [property: JsonPropertyName("capability")] string? Capability,
    [property: JsonPropertyName("failureStages")] IReadOnlyList<string>? FailureStages);

internal sealed record ArchiveTestLimits(
    [property: JsonPropertyName("entryCount")] int EntryCount,
    [property: JsonPropertyName("expandedBytesBudget")] long ExpandedBytesBudget,
    [property: JsonPropertyName("jsonBytesBudget")] long JsonBytesBudget,
    [property: JsonPropertyName("deadlineSeconds")] int DeadlineSeconds);

internal sealed record ArchiveTestCase(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("generatorContractVersion")] string GeneratorContractVersion,
    [property: JsonPropertyName("generatorVersion")] string GeneratorVersion,
    [property: JsonPropertyName("fixtureId")] string FixtureId,
    [property: JsonPropertyName("caseKey")] string CaseKey,
    [property: JsonPropertyName("caseRevision")] int CaseRevision,
    [property: JsonPropertyName("expectationRevision")] int ExpectationRevision,
    [property: JsonPropertyName("seed")] int Seed,
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("archive")] ArchiveTestArchive Archive,
    [property: JsonPropertyName("entries")] IReadOnlyList<ArchiveTestEntry> Entries,
    [property: JsonPropertyName("mutations")] IReadOnlyList<ArchiveTestMutation> Mutations,
    [property: JsonPropertyName("expectations")] IReadOnlyList<ArchiveTestExpectation> Expectations,
    [property: JsonPropertyName("limits")] ArchiveTestLimits Limits);

/// <summary>
/// Semantic checks that the draft-07 schema cannot express (REQ-217): basename ↔ Fixture ID
/// equality, unique entry ordinals, declared limits against the v1 budgets (REQ-213), required
/// operation outcomes (REQ-212), safe output basenames (REQ-209), and enum/length sanity.
/// </summary>
internal static class ArchiveTestCaseSemantics
{
    internal const long MaxArchivePhysicalBytes = 16 * 1024 * 1024;
    internal const long MaxExpandedBytesBudget = 32 * 1024 * 1024;
    internal const int MaxEntries = 1_000;
    internal const long MaxJsonBytes = 1024 * 1024;
    internal const int MaxDeadlineSeconds = 10;

    private static readonly string[] Classifications = ["valid", "malformed", "policy-sensitive"];
    private static readonly string[] Kinds = ["file", "directory"];
    private static readonly string[] Operations = ["list", "read-entry", "integrity-check", "extract"];
    private static readonly string[] Structures =
    [
        "local-header", "file-data", "data-descriptor", "central-header", "central-directory", "eocd", "comment", "whole-archive"
    ];

    internal static IReadOnlyList<string> Validate(ArchiveTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        var errors = new List<string>();

        // Parsed JSON may deliver null sections despite non-nullable annotations;
        // reject them as validation errors, not null-reference exceptions.
        if (testCase.Archive is null)
        {
            errors.Add("archive section must not be null");
        }

        if (testCase.Entries is null)
        {
            errors.Add("entries section must not be null");
        }

        if (testCase.Mutations is null)
        {
            errors.Add("mutations section must not be null");
        }

        if (testCase.Expectations is null)
        {
            errors.Add("expectations section must not be null");
        }

        if (testCase.Limits is null)
        {
            errors.Add("limits section must not be null");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        if (testCase.SchemaVersion != 1)
        {
            errors.Add($"schemaVersion must be 1, got {testCase.SchemaVersion}");
        }

        if (!Classifications.Contains(testCase.Classification ?? string.Empty, StringComparer.Ordinal))
        {
            errors.Add($"unknown classification '{testCase.Classification}'");
        }

        if (!ArchiveTestIdentity.IsFixtureId(testCase.FixtureId ?? string.Empty))
        {
            errors.Add($"fixtureId '{testCase.FixtureId}' is not atc- plus 64 lowercase hex characters");
        }

        var archive = testCase.Archive!;
        if (archive.FileName != testCase.FixtureId + ".zip")
        {
            errors.Add("archive.fileName must equal fixtureId + \".zip\"");
        }

        if (IsUnsafeBasename(archive.FileName ?? string.Empty))
        {
            errors.Add($"archive.fileName '{archive.FileName}' must be a bare basename without path separators");
        }

        if (!ArchiveTestIdentity.IsSha256Hex(archive.Sha256 ?? string.Empty))
        {
            errors.Add("archive.sha256 must be 64 lowercase hex characters");
        }

        if (archive.PhysicalSize < 0 || archive.PhysicalSize > MaxArchivePhysicalBytes)
        {
            errors.Add($"archive.physicalSize must be between 0 and {MaxArchivePhysicalBytes}");
        }

        var seenOrdinals = new HashSet<int>();
        foreach (var entry in testCase.Entries!)
        {
            if (entry is null)
            {
                errors.Add("entry must not be null");
                continue;
            }

            if (entry.Ordinal < 0 || !seenOrdinals.Add(entry.Ordinal))
            {
                errors.Add($"entry ordinal {entry.Ordinal} is negative or duplicate");
            }

            if (!Kinds.Contains(entry.Kind ?? string.Empty, StringComparer.Ordinal))
            {
                errors.Add($"entry {entry.Ordinal}: unknown kind '{entry.Kind}'");
            }

            if (!IsBoundedHex(entry.LocalNameRaw ?? string.Empty, int.MaxValue)
                || !IsBoundedHex(entry.CentralNameRaw ?? string.Empty, int.MaxValue))
            {
                errors.Add($"entry {entry.Ordinal}: name bytes must be lowercase hex with even length");
            }
        }

        if (testCase.Entries!.Count > MaxEntries)
        {
            errors.Add($"entries exceed the {MaxEntries}-entry budget");
        }

        foreach (var mutation in testCase.Mutations!)
        {
            if (mutation is null)
            {
                errors.Add("mutation must not be null");
                continue;
            }

            if (string.IsNullOrEmpty(mutation.Code))
            {
                errors.Add("mutation code must not be empty");
            }

            if (!Structures.Contains(mutation.Structure ?? string.Empty, StringComparer.Ordinal))
            {
                errors.Add($"mutation '{mutation.Code}': unknown structure '{mutation.Structure}'");
            }

            if (mutation.OffsetBasis != "before-mutation")
            {
                errors.Add($"mutation '{mutation.Code}': offsetBasis must be before-mutation");
            }

            if (mutation.Offset is < 0 || mutation.Offset > archive.PhysicalSize)
            {
                errors.Add($"mutation '{mutation.Code}': offset must be between 0 and archive.physicalSize");
            }

            if (mutation.DeletedLength is null && mutation.InsertedLength is null)
            {
                errors.Add($"mutation '{mutation.Code}': must declare deletedLength or insertedLength");
            }

            if (mutation.DeletedLength is < 0 || mutation.InsertedLength is < 0)
            {
                errors.Add($"mutation '{mutation.Code}': lengths must not be negative");
            }

            if (mutation.DeletedLength > archive.PhysicalSize - mutation.Offset)
            {
                errors.Add($"mutation '{mutation.Code}': deletedLength exceeds the bytes available at offset");
            }

            if (mutation.BeforeSize is < 0 || mutation.AfterSize is < 0)
            {
                errors.Add($"mutation '{mutation.Code}': beforeSize and afterSize must not be negative");
            }

            if (!IsOptionalBoundedHex(mutation.BeforeHex, 256) || !IsOptionalBoundedHex(mutation.AfterHex, 256))
            {
                errors.Add($"mutation '{mutation.Code}': inline hex must be lowercase hex with even length and at most 256 bytes");
            }
        }

        if (testCase.Expectations!.Count == 0)
        {
            errors.Add("expectations must contain at least one operation record");
        }

        foreach (var expectation in testCase.Expectations!)
        {
            if (expectation is null)
            {
                errors.Add("expectation must not be null");
                continue;
            }

            if (!Operations.Contains(expectation.Operation ?? string.Empty, StringComparer.Ordinal))
            {
                errors.Add($"unknown operation '{expectation.Operation}'");
            }

            if (string.IsNullOrEmpty(expectation.Profile))
            {
                errors.Add($"operation '{expectation.Operation}': profile must not be empty");
            }

            if (expectation.AllowedOutcomes is null || expectation.AllowedOutcomes.Count == 0)
            {
                errors.Add($"operation '{expectation.Operation}': allowedOutcomes must not be empty");
            }
        }

        var limits = testCase.Limits!;
        if (limits.EntryCount != testCase.Entries!.Count)
        {
            errors.Add($"limits.entryCount {limits.EntryCount} does not match {testCase.Entries.Count} entries");
        }

        if (limits.ExpandedBytesBudget < 0 || limits.ExpandedBytesBudget > MaxExpandedBytesBudget)
        {
            errors.Add($"limits.expandedBytesBudget must be between 0 and {MaxExpandedBytesBudget}");
        }

        if (limits.JsonBytesBudget < 1 || limits.JsonBytesBudget > MaxJsonBytes)
        {
            errors.Add($"limits.jsonBytesBudget must be between 1 and {MaxJsonBytes}");
        }

        if (limits.DeadlineSeconds is < 1 or > MaxDeadlineSeconds)
        {
            errors.Add($"limits.deadlineSeconds must be between 1 and {MaxDeadlineSeconds}");
        }

        return errors;
    }

    private static bool IsOptionalBoundedHex(string? value, int maxBytes) =>
        value is null || IsBoundedHex(value, maxBytes);

    private static bool IsBoundedHex(string value, int maxBytes) =>
        value.Length % 2 == 0
        && value.Length <= maxBytes * 2
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsUnsafeBasename(string fileName) =>
        fileName.Length == 0
        || fileName.Contains('/')
        || fileName.Contains('\\')
        || fileName.Contains('\0')
        || fileName.Contains(':')
        || fileName.StartsWith(".", StringComparison.Ordinal);
}
