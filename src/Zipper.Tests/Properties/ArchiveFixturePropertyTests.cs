using System.Buffers.Binary;
using System.IO.Compression;
using FsCheck.Xunit;
using Xunit;
using Zipper.ArchiveTests;
using Zipper.Cli.Modules;

namespace Zipper.Tests.Properties;

/// <summary>
/// Property-based tests for Archive Test Fixtures, layout structure preservation,
/// mutation invariants, reader operation semantics, and CLI module suite handling.
/// </summary>
[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class ArchiveFixturePropertyTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ArchiveTestCaseDefinition>> SuiteCasesByCanonicalName =
        ArchiveTestCatalog.SupportedSuites.ToDictionary(
            s => s,
            ArchiveTestCatalog.ListSuite,
            StringComparer.Ordinal);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArchiveTestCase> ExpectationCaseCache = new(StringComparer.Ordinal);

    [Property]
    public void ArchiveFixtureLayout_LocateStructures_ShouldPreserveHeaderAndCentralDirectoryOffsets(ArchiveFixturePropertyInput input)
    {
        var recipeEntries = new List<ArchiveTestRecipeEntry>(input.Entries.Length);
        for (var i = 0; i < input.Entries.Length; i++)
        {
            var spec = input.Entries[i];
            var name = $"f_{i}_{spec.CompressionMethod}.dat";
            recipeEntries.Add(ArchiveTestRecipeEntry.File(name, spec.PayloadLength, spec.PayloadCodec));
        }

        var definition = new ArchiveTestCaseDefinition(
            "layout-property-case",
            1,
            1,
            "valid",
            ["smoke"],
            new ArchiveTestRecipe(recipeEntries));

        var artifact = ArchiveFixtureBuilder.Build(definition, input.Seed, CancellationToken.None);
        var layout = ArchiveFixtureLayout.Read(artifact.ArchiveBytes);

        Assert.Equal(artifact.ArchiveBytes.Length, layout.TotalBytes);
        Assert.Equal(recipeEntries.Count, layout.Entries.Count);
        Assert.True(layout.CentralDirectoryOffset >= 0);
        Assert.True(layout.CentralDirectorySize > 0);
        Assert.True(layout.CentralDirectoryOffset + layout.CentralDirectorySize <= layout.EocdOffset);
        Assert.True(layout.EocdOffset + 22 <= layout.TotalBytes);

        // Local file headers strictly ordered and non-overlapping
        for (var i = 0; i < layout.Entries.Count; i++)
        {
            var entry = layout.Entries[i];
            Assert.Equal(i, entry.Ordinal);
            Assert.True(entry.LocalHeaderOffset >= 0);
            Assert.Equal(
                ArchiveFixtureLayout.LocalHeaderSignature,
                BinaryPrimitives.ReadUInt32LittleEndian(artifact.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset, 4)));

            var localHeaderEnd = entry.LocalHeaderOffset + 30 + entry.LocalNameLength + entry.LocalExtraLength;
            Assert.Equal(localHeaderEnd, entry.DataOffset);

            var entrySpanEnd = entry.DataOffset + entry.CompressedSize;
            if (entry.HasDataDescriptor)
            {
                Assert.Equal(entrySpanEnd, entry.DataDescriptorOffset);
                entrySpanEnd += entry.DataDescriptorOffset > 0 ? 16 : 0;
            }

            Assert.True(entrySpanEnd <= layout.CentralDirectoryOffset);

            if (i > 0)
            {
                var prev = layout.Entries[i - 1];
                Assert.True(prev.LocalHeaderOffset < entry.LocalHeaderOffset, "Local headers must be strictly ordered.");
                var prevSpanEnd = prev.DataOffset + prev.CompressedSize + (prev.HasDataDescriptor && prev.DataDescriptorOffset > 0 ? 16 : 0);
                Assert.True(prevSpanEnd <= entry.LocalHeaderOffset, "Local entry spans must be non-overlapping.");
            }
        }

        // Central directory headers strictly ordered and non-overlapping
        for (var i = 0; i < layout.Entries.Count; i++)
        {
            var entry = layout.Entries[i];
            Assert.True(entry.CentralDirectoryOffset >= layout.CentralDirectoryOffset);
            Assert.True(entry.CentralDirectoryOffset < layout.CentralDirectoryOffset + layout.CentralDirectorySize);
            Assert.Equal(
                ArchiveFixtureLayout.CentralHeaderSignature,
                BinaryPrimitives.ReadUInt32LittleEndian(artifact.ArchiveBytes.AsSpan((int)entry.CentralDirectoryOffset, 4)));

            if (i > 0)
            {
                var prev = layout.Entries[i - 1];
                Assert.True(prev.CentralDirectoryOffset < entry.CentralDirectoryOffset, "Central headers must be strictly ordered.");
                var prevCentralEnd = prev.CentralDirectoryOffset + 46 + prev.CentralNameLength + prev.CentralExtraLength + prev.CentralCommentLength;
                Assert.True(prevCentralEnd <= entry.CentralDirectoryOffset, "Central headers must be non-overlapping.");
            }
        }

        // EOCD signature check
        Assert.Equal(
            ArchiveFixtureLayout.EocdSignature,
            BinaryPrimitives.ReadUInt32LittleEndian(artifact.ArchiveBytes.AsSpan((int)layout.EocdOffset, 4)));
    }

    [Property]
    public void ArchiveFixtureLayout_Read_ShouldPreserveCodecDivergenceWithoutShiftingOffsets(ArchiveFixturePropertyInput input)
    {
        const string LogicalName = "§.txt";
        var baselineDefinition = new ArchiveTestCaseDefinition(
            "encoding-codec-divergence-baseline",
            CaseRevision: 1,
            ExpectationRevision: 1,
            Classification: "valid",
            Suites: [ArchiveTestCatalog.EncodingSuite],
            Recipe: new ArchiveTestRecipe([ArchiveTestRecipeEntry.File(LogicalName, 60, "stored")]));
        var baseline = ArchiveFixtureBuilder.Build(baselineDefinition, input.Seed, CancellationToken.None);
        var artifact = ArchiveFixtureBuilder.BuildControl(
            "encoding-local-central-codec-divergence", input.Seed, CancellationToken.None);
        var entry = Assert.Single(ArchiveFixtureLayout.Read(
            artifact.ArchiveBytes, allowLocalCentralNameDivergence: true).Entries);

        Assert.Equal("81982e747874", entry.LocalNameHex);
        Assert.Equal("c2a72e747874", entry.NameHex);
        Assert.Equal(LogicalName, entry.Name);
        Assert.Equal(baseline.Layout.Entries[0].DataOffset, entry.DataOffset);
        Assert.Equal(baseline.Layout.CentralDirectoryOffset, artifact.Layout.CentralDirectoryOffset);
        Assert.Equal(baseline.ArchiveBytes.Length, artifact.ArchiveBytes.Length);
    }

    [Property]
    public void ArchiveFixtureBuilder_Build_ShouldRoundTripMethod8StoredBlockStream(DeflateStoredBlockPropertyInput input)
    {
        const string CaseKey = "deflate-stored-block-property";
        var definition = new ArchiveTestCaseDefinition(
            CaseKey,
            CaseRevision: 1,
            ExpectationRevision: 1,
            Classification: "valid",
            Suites: [ArchiveTestCatalog.CompatibilitySuite],
            Recipe: new ArchiveTestRecipe(
            [
                ArchiveTestRecipeEntry.File("stored-block.bin", input.PayloadLength, "deflate"),
            ]),
            Construction: ArchiveControlConstruction.DeflateStoredBlock);

        var artifact = ArchiveFixtureBuilder.Build(definition, input.Seed, CancellationToken.None);
        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal((ushort)8, entry.Method);

        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        using var stream = Assert.Single(archive.Entries).Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        Assert.Equal(artifact.Entries[0].Content, copy.ToArray());
        Assert.Equal((uint)artifact.Entries[0].Content.Length, entry.UncompressedSize);
    }

    [Property]
    public void ArchiveFixtureMutator_ApplyMutation_ShouldModifyOnlyTargetedStructureAndPreserveUnmutatedRegions(ArchiveFixturePropertyInput input)
    {
        var targetCase = ZipperArbitraries.SingleMutationMalformedCases[
            Math.Clamp(input.SingleMutationCaseIndex, 0, ZipperArbitraries.SingleMutationMalformedCases.Count - 1)];
        var controlCase = ArchiveTestCatalog.GetCase(targetCase.ControlCaseKey!);
        if (targetCase.ControlConstruction is { } controlConstruction)
        {
            controlCase = controlCase with { Construction = controlConstruction };
        }

        var control = ArchiveFixtureBuilder.Build(controlCase, input.Seed, CancellationToken.None);
        var mutationKind = targetCase.Mutations![0];
        var mutated = ArchiveFixtureMutator.Apply(mutationKind, control);

        Assert.NotEmpty(mutated.Mutations);
        Assert.False(control.ArchiveBytes.AsSpan().SequenceEqual(mutated.ArchiveBytes));
        Assert.NotEqual(control.ArchiveSha256, mutated.ArchiveSha256);

        var firstMutation = mutated.Mutations[0];
        Assert.Equal("before-mutation", firstMutation.OffsetBasis);
        Assert.True(firstMutation.Offset >= 0 && firstMutation.Offset <= control.ArchiveBytes.Length);
        Assert.Equal(control.ArchiveBytes.Length, firstMutation.BeforeSize);
        Assert.Equal(mutated.ArchiveBytes.Length, firstMutation.AfterSize);
        Assert.Equal(control.ArchiveSha256, firstMutation.BeforeSha256);
        Assert.Equal(mutated.ArchiveSha256, firstMutation.AfterSha256);
        Assert.False(string.IsNullOrEmpty(firstMutation.Structure));

        // Preserve unmutated regions: for any entry whose local record is prior to mutation offset,
        // its local header and payload bytes must remain byte-identical.
        foreach (var entry in control.Layout.Entries)
        {
            var entryEnd = (int)(entry.DataOffset + entry.CompressedSize);
            if (entryEnd <= firstMutation.Offset && firstMutation.Structure != "whole-archive")
            {
                var entryLen = entryEnd - (int)entry.LocalHeaderOffset;
                var beforeSpan = control.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset, entryLen);
                var afterSpan = mutated.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset, entryLen);
                Assert.True(beforeSpan.SequenceEqual(afterSpan), $"Entry {entry.Ordinal} bytes before mutation offset must be preserved.");
            }
            else if (firstMutation.Ordinal is { } targetOrdinal && entry.Ordinal != targetOrdinal && control.ArchiveBytes.Length == mutated.ArchiveBytes.Length)
            {
                // For same-size mutations targeting another entry, unrelated entry payloads remain byte-identical
                var payloadLen = (int)entry.CompressedSize;
                var beforePayload = control.ArchiveBytes.AsSpan((int)entry.DataOffset, payloadLen);
                var afterPayload = mutated.ArchiveBytes.AsSpan((int)entry.DataOffset, payloadLen);
                Assert.True(beforePayload.SequenceEqual(afterPayload), $"Unrelated entry {entry.Ordinal} payload must remain byte-identical.");
            }
        }
    }

    [Property]
    public void ArchiveTestCaseSemantics_EvaluateReaderOperations_ShouldEnforceFourReaderOperationsIndependently(ArchiveFixturePropertyInput input)
    {
        var suiteCases = SuiteCasesByCanonicalName[input.SuiteName];
        var caseDefinition = suiteCases[Math.Clamp(input.SuiteCaseIndex, 0, suiteCases.Count - 1)];
        var testCase = ExpectationCaseCache.GetOrAdd(
            caseDefinition.CaseKey,
            _ => ArchiveTestSuiteGenerator.BuildExpectationFile(
                ArchiveFixtureBuilder.Build(caseDefinition, input.Seed, CancellationToken.None),
                caseDefinition));

        var errors = ArchiveTestCaseSemantics.Validate(testCase);
        Assert.Empty(errors);

        var distinctOps = testCase.Expectations.Select(e => e.Operation).Distinct(StringComparer.Ordinal).ToList();
        foreach (var op in ArchiveTestCaseSemantics.Operations)
        {
            Assert.Contains(op, distinctOps);
        }

        // Profile monotonicity invariants
        var strictExpectations = testCase.Expectations.Where(e => e.Profile == "strict").ToList();
        if (strictExpectations.Count > 0 && caseDefinition.Classification == "valid")
        {
            Assert.Contains(strictExpectations, e => e.Operation == "list" && e.AllowedOutcomes.Contains("listed-count-matches-entries"));
            Assert.Contains(strictExpectations, e => e.Operation == "read-entry" && e.AllowedOutcomes.Contains("read-entry-content-matches"));
            Assert.Contains(strictExpectations, e => e.Operation == "integrity-check" && e.AllowedOutcomes.Contains("integrity-passes"));
            Assert.Contains(strictExpectations, e => e.Operation == "extract" && e.AllowedOutcomes.Contains("extract-completes"));
        }

        var fullCodecExpectations = testCase.Expectations.Where(e => e.Profile == "full-codec").ToList();
        if (fullCodecExpectations.Count > 0 && caseDefinition.Classification == "valid")
        {
            Assert.Contains(fullCodecExpectations, e => e.Operation == "list" && e.AllowedOutcomes.Contains("listed-count-matches-entries"));
            Assert.Contains(fullCodecExpectations, e => e.Operation == "read-entry" && e.AllowedOutcomes.Contains("read-entry-content-matches"));
            Assert.Contains(fullCodecExpectations, e => e.Operation == "integrity-check" && e.AllowedOutcomes.Contains("integrity-passes"));
            Assert.Contains(fullCodecExpectations, e => e.Operation == "extract" && e.AllowedOutcomes.Contains("extract-completes"));
        }

        var unsupportedExpectations = testCase.Expectations.Where(e => e.Profile == "unsupported-reader").ToList();
        if (unsupportedExpectations.Count > 0 && caseDefinition.Classification == "valid")
        {
            Assert.Contains(unsupportedExpectations, e => e.Operation == "list" && e.AllowedOutcomes.Contains("listed-count-matches-entries"));
            Assert.Contains(unsupportedExpectations, e => e.Operation == "read-entry" && e.AllowedOutcomes.Contains("unsupported-method-rejected") && !e.AllowedOutcomes.Contains("read-entry-content-matches"));
            Assert.Contains(unsupportedExpectations, e => e.Operation == "integrity-check" && e.AllowedOutcomes.Contains("unsupported-method-rejected") && !e.AllowedOutcomes.Contains("integrity-passes"));
            Assert.Contains(unsupportedExpectations, e => e.Operation == "extract" && e.AllowedOutcomes.Contains("unsupported-method-rejected") && !e.AllowedOutcomes.Contains("extract-completes"));
        }

        // Independent operation enforcement: removing any single operation triggers specific validation error
        foreach (var op in ArchiveTestCaseSemantics.Operations)
        {
            var withoutOp = testCase with
            {
                Expectations = [.. testCase.Expectations.Where(e => e.Operation != op)]
            };
            var opErrors = ArchiveTestCaseSemantics.Validate(withoutOp);
            Assert.Contains(opErrors, e => e.Contains($"expectations must declare operation '{op}'", StringComparison.Ordinal));
        }
    }

    [Property]
    public void ArchiveTestModule_TryApplyAndTryBuild_ShouldAcceptAllCanonicalSuitesAndRejectUnknownSuites(ArchiveFixturePropertyInput input)
    {
        var module = new ArchiveTestModule();
        var applied = module.TryApply("--archive-test-suite", input.RawSuiteInput);
        Assert.True(applied);
        Assert.Equal(input.RawSuiteInput, module.RawSuite);
        Assert.True(module.IsRequested);

        var resolved = ArchiveTestCatalog.ResolveSuite(module.RawSuite!);
        if (input.IsCanonicalSuite)
        {
            Assert.NotNull(resolved);
            Assert.Contains(resolved, ArchiveTestCatalog.SupportedSuites, StringComparer.Ordinal);
            Assert.Equal(input.SuiteName, resolved, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Null(resolved);
        }
    }
}
