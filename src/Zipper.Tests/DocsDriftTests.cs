using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class DocsDriftTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                && File.Exists(Path.Combine(dir.FullName, "Requirements.md"))
                && File.Exists(Path.Combine(dir.FullName, "docs", "architecture.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate repo root from current working directory.");
    }

    [Fact]
    public void README_LoadFileFormat_IncludesXmlAlias()
    {
        var readmePath = Path.Combine(RepoRoot, "README.md");
        var lines = File.ReadAllLines(readmePath);

        var loadFileFormatLine = lines
            .FirstOrDefault(l => l.TrimStart().StartsWith("| `--load-file-format`", StringComparison.Ordinal)
                              && l.IndexOf("dat", StringComparison.Ordinal) >= 0);

        Assert.NotNull(loadFileFormatLine);
        var line = loadFileFormatLine;
        Assert.True(line.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0, $"Expected 'xml' alias in README load-file-format line: {line}");
        Assert.True(line.IndexOf("edrm-xml", StringComparison.OrdinalIgnoreCase) >= 0, $"Expected 'edrm-xml' alias in README load-file-format line: {line}");
    }

    [Fact]
    public void Requirements_REQ186_CrossRef_DoesNotPointToPostGenerationValidation()
    {
        var reqPath = Path.Combine(RepoRoot, "Requirements.md");
        var lines = File.ReadAllLines(reqPath);

        var req186Line = lines.FirstOrDefault(l => l.StartsWith("- **REQ-186**:", StringComparison.Ordinal));
        Assert.NotNull(req186Line);

        Assert.True(req186Line.IndexOf("REQ-148", StringComparison.Ordinal) < 0, $"REQ-186 should not cite REQ-148: {req186Line}");
        Assert.True(req186Line.IndexOf("FR-023", StringComparison.Ordinal) >= 0, $"REQ-186 should cite FR-023: {req186Line}");
        Assert.True(req186Line.IndexOf("§8.2", StringComparison.Ordinal) >= 0, $"REQ-186 should cite §8.2: {req186Line}");
    }

    [Fact]
    public void README_CompressionRow_MarksDeflate64Bzip2AsNotYetSupported()
    {
        var readmePath = Path.Combine(RepoRoot, "README.md");
        var lines = File.ReadAllLines(readmePath);

        // Anchor on the Quick Reference table-row format. Matching any line that mentions both
        // "--compression" and "store" would pick up a usage example elsewhere in the README,
        // which does not carry the argument's value list.
        var compressionLine = lines
            .FirstOrDefault(l => l.TrimStart().StartsWith("| `--compression`", StringComparison.Ordinal));

        Assert.NotNull(compressionLine);
        var line = compressionLine;
        Assert.True(line.IndexOf("deflate64", StringComparison.OrdinalIgnoreCase) >= 0, $"Expected 'deflate64' mentioned in README compression line: {line}");
        Assert.True(line.IndexOf("bzip2", StringComparison.OrdinalIgnoreCase) >= 0, $"Expected 'bzip2' mentioned in README compression line: {line}");
        Assert.True(line.IndexOf("planned", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("not yet supported", StringComparison.OrdinalIgnoreCase) >= 0,
            $"Expected deflate64/bzip2 marked planned/unsupported in README compression line: {line}");
    }

    [Fact]
    public void Architecture_ProductionSet_LoadFileDelegation_UsesOrchestrator()
    {
        var archPath = Path.Combine(RepoRoot, "docs", "architecture.md");
        var content = File.ReadAllText(archPath);

        Assert.True(content.IndexOf("ProductionSetOrchestrator", StringComparison.Ordinal) >= 0, "architecture.md should reference ProductionSetOrchestrator for Production Set load-file delegation.");
        Assert.True(content.IndexOf("PSG --> LFO", StringComparison.Ordinal) < 0, "architecture.md should not show PSG --> LFO; PSG is a thin facade.");
    }

    [Fact]
    public void ArchiveTestSuiteOptions_DocumentEverySupportedSuite()
    {
        var supported = ArchiveTestCatalog.SupportedSuites;
        Assert.NotEmpty(supported);
        var option = $"<{string.Join("|", supported)}>";
        var readmeRow = Assert.Single(File.ReadLines(Path.Combine(RepoRoot, "README.md")),
            line => line.StartsWith("| `--archive-test-suite`", StringComparison.Ordinal));
        Assert.Contains(string.Join(", ", supported.Select(suite => $"`{suite}`")), readmeRow);

        var requirement = Assert.Single(File.ReadLines(Path.Combine(RepoRoot, "Requirements.md")),
            line => line.StartsWith("- **REQ-208**:", StringComparison.Ordinal));
        Assert.Contains($"--archive-test-suite {option}", requirement);

        var docs = File.ReadAllText(Path.Combine(RepoRoot, "docs", "archive-test-suites.md"));
        Assert.Contains($"--archive-test-suite {option}", docs);
        foreach (var suite in supported)
        {
            Assert.Contains($"  - `{suite}` —", docs);
        }
    }

    [Fact]
    public void ArchiveTestSuites_ExpectationFileSchema_DocumentsCodecAndWireMethodFields()
    {
        var docs = File.ReadAllText(Path.Combine(RepoRoot, "docs", "archive-test-suites.md"));
        Assert.Contains("localHeaderMethod", docs);
        Assert.Contains("centralDirectoryMethod", docs);
        Assert.Contains("payloadCodec", docs);
        Assert.Contains("stored", docs);
        Assert.Contains("deflate", docs);
        Assert.Contains("deflate64", docs);
        Assert.Contains("bzip2", docs);
        Assert.Contains("unknown", docs);
        Assert.Contains("omitted for directories", docs);
    }

    [Fact]
    public void Requirements_Section8ColumnLists_IncludesCompressionMethod()
    {
        var reqPath = Path.Combine(RepoRoot, "Requirements.md");
        var content = File.ReadAllText(reqPath);

        Assert.Contains("Compression Method", content);
        Assert.Contains("COMPRESSION_METHOD", content);
        Assert.Contains("COMPRESSION METHOD", content);
    }

    [Fact]
    public void UbiquitousLanguage_RecentTerminology_ContainsNewTerms()
    {
        var langPath = Path.Combine(RepoRoot, "UBIQUITOUS_LANGUAGE.md");
        var content = File.ReadAllText(langPath);

        Assert.Contains("**Archive Test Suite**", content);
        Assert.Contains("**Mutation**", content);
        Assert.Contains("**Reader Profile**", content);
        Assert.Contains("**Capability Profile**", content);
        Assert.Contains("**Frozen Test Vector**", content);
        Assert.Contains("**Collection Metadata**", content);
        Assert.Contains("**Document Family**", content);
        Assert.Contains("**Compression Method**", content);
        Assert.Contains("**Local Header Method**", content);
        Assert.Contains("**Central Directory Method**", content);
        Assert.Contains("**Payload Codec**", content);
        Assert.Contains("**Field Naming Convention**", content);
        Assert.Contains("**New Production Manifest**", content);
        Assert.Contains("**Volume Membership**", content);
        Assert.Contains("**Production Metadata Budget**", content);
        Assert.Contains("**Validation Report**", content);
    }

    [Fact]
    public void Architecture_ComponentMapAndOverview_DocumentsTwelveModulesAndValidationOrchestrator()
    {
        var archPath = Path.Combine(RepoRoot, "docs", "architecture.md");
        var content = File.ReadAllText(archPath);

        Assert.Contains("all twelve", content);
        Assert.Contains("ValidationOrchestrator", content);
    }

    [Fact]
    public void TestsReadme_E2eScriptIndex_IsInSyncWithRunTestsSh()
    {
        var runTestsShPath = Path.Combine(RepoRoot, "tests", "run-tests.sh");
        var runTestsLines = File.ReadAllLines(runTestsShPath);

        var invokedWithBatParity = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in runTestsLines)
        {
            var line = rawLine.TrimStart();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var match = System.Text.RegularExpressions.Regex.Match(
                line,
                @"bash \./tests/(test-[a-zA-Z0-9._-]+)\.sh",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1));

            if (match.Success)
            {
                var scriptName = match.Groups[1].Value;
                var batPath = Path.Combine(RepoRoot, "tests", $"{scriptName}.bat");
                if (File.Exists(batPath))
                {
                    invokedWithBatParity.Add(scriptName);
                }
            }
        }

        Assert.NotEmpty(invokedWithBatParity);

        var readmePath = Path.Combine(RepoRoot, "tests", "README.md");
        var readmeLines = File.ReadAllLines(readmePath);

        var indexedScripts = new SortedSet<string>(StringComparer.Ordinal);
        var inIndexSection = false;
        foreach (var rawLine in readmeLines)
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("## E2E Script Index", StringComparison.Ordinal))
            {
                inIndexSection = true;
                continue;
            }

            if (inIndexSection && line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (inIndexSection && line.StartsWith('|'))
            {
                if (line.StartsWith("| Script", StringComparison.OrdinalIgnoreCase) || line.StartsWith("|---", StringComparison.Ordinal))
                {
                    continue;
                }

                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"^\|\s*`([^`]+)`\s*\|",
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(1));
                if (match.Success)
                {
                    indexedScripts.Add(match.Groups[1].Value);
                }
            }
        }

        Assert.NotEmpty(indexedScripts);

        var missingInReadme = invokedWithBatParity.Except(indexedScripts).ToList();
        var extraInReadme = indexedScripts.Except(invokedWithBatParity).ToList();

        Assert.True(missingInReadme.Count == 0, $"Scripts called by run-tests.sh with .bat parity missing from tests/README.md: {string.Join(", ", missingInReadme)}");
        Assert.True(extraInReadme.Count == 0, $"Scripts listed in tests/README.md not called by run-tests.sh with .bat parity: {string.Join(", ", extraInReadme)}");
    }

    [Fact]
    public void Requirements_Section17_ContainsHelpTextGeneratorContractNote()
    {
        var reqPath = Path.Combine(RepoRoot, "Requirements.md");
        var content = File.ReadAllText(reqPath);
        Assert.Contains("HelpTextGenerator", content);
        Assert.Contains("CLI Help Text Contract", content);
    }

    [Fact]
    public void TraceabilityManifest_ContainsMappingsForReverseCoverageSeams()
    {
        var tsvPath = Path.Combine(RepoRoot, "tests", "req-traceability.tsv");
        var lines = File.ReadAllLines(tsvPath);

        string[] requiredSeams =
        [
            "ArchiveCombinedCaseTests.",
            "ArchiveCompatibilityCaseTests.",
            "ArchiveStructureCaseTests.",
            "ArchivePathPolicyCaseTests.",
            "ArchiveResourceCaseTests.",
            "ArchiveFixtureLayoutTests.",
            "ArchiveTestCodecTests.",
            "ArchiveTestJsonTests.",
            "Deflate64LongMatchTests.",
            "ColumnCountValidatorTests.",
            "LineEndingValidatorTests.",
            "OptBoundaryValidatorTests.",
            "StandardModeTests.",
            "ChaosEngineBuilderTests.",
            "EmailFactoryTests.",
            "ProductionSetOrchestratorTests.",
            "HelpTextGeneratorTests.",
        ];

        foreach (var seam in requiredSeams)
        {
            var matched = lines.Any(line =>
            {
                var parts = line.Split('\t');
                return parts.Length >= 3 && parts[1] == "unit" && parts[2].StartsWith(seam, StringComparison.Ordinal);
            });

            Assert.True(matched, $"req-traceability.tsv should contain unit test reference for seam: {seam}");
        }
    }

    [Fact]
    public void TraceabilityManifest_REQ030_MapsToLiveVersionTestsAndRetiresComputeVersion()
    {
        var tsvPath = Path.Combine(RepoRoot, "tests", "req-traceability.tsv");
        var lines = File.ReadAllLines(tsvPath);

        var req030Lines = lines.Where(l => l.StartsWith("REQ-030\t", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(req030Lines);
        Assert.DoesNotContain(req030Lines, l => l.Contains("test-compute-version.sh"));
        Assert.Contains(req030Lines, l => l.Contains("test-build-version.sh"));
        Assert.Contains(req030Lines, l => l.Contains("ci/test_release.py"));

        var computeVersionScript = Path.Combine(RepoRoot, ".github", "scripts", "compute-version.sh");
        var computeVersionTest = Path.Combine(RepoRoot, "tests", "test-compute-version.sh");
        Assert.False(File.Exists(computeVersionScript), ".github/scripts/compute-version.sh should be retired");
        Assert.False(File.Exists(computeVersionTest), "tests/test-compute-version.sh should be retired");

        var testsReadmePath = Path.Combine(RepoRoot, "tests", "README.md");
        var testsReadme = File.ReadAllText(testsReadmePath);
        Assert.DoesNotContain("test-compute-version.sh", testsReadme);
    }
}

