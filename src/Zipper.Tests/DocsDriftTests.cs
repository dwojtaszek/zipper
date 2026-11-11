using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class DocsDriftTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void README_DocumentationIndex_LinksEveryTopLevelGuideAndAdrIndex()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, "README.md"));
        var start = Array.IndexOf(lines, "## Documentation Index");
        Assert.True(start >= 0, "Missing README Documentation Index.");
        var index = string.Join('\n', lines.Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)));
        var guides = Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "*.md")
            .Select(path => $"docs/{Path.GetFileName(path)}")
            .Append("CI.md")
            .Append("docs/adr/README.md");

        foreach (var guide in guides)
        {
            Assert.Contains($"]({guide})", index);
            Assert.True(File.Exists(Path.Combine(RepoRoot, guide)), $"Missing guide: {guide}");
        }
    }

    [Theory]
    [InlineData("FR-035: Production Manifest Comparison", 157, 163)]
    [InlineData("FR-036: Hash Generation", 165, 169)]
    public void Requirements_ComparisonAndHashRequirements_HaveOwnGroups(string heading, int first, int last)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, "Requirements.md"));
        var start = Array.IndexOf(lines, $"### {heading}");
        Assert.True(start >= 0, $"Missing requirement group: {heading}");
        var group = lines.Skip(start + 1).TakeWhile(line => !line.StartsWith("##", StringComparison.Ordinal)).ToArray();

        for (var id = first; id <= last; id++)
        {
            Assert.Contains(group, line => line.StartsWith($"- **REQ-{id}**:", StringComparison.Ordinal));
        }
    }

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
    public void AuditFileDefinitions_CoverEveryGenerationMode()
    {
        var languageLines = File.ReadAllLines(Path.Combine(RepoRoot, "UBIQUITOUS_LANGUAGE.md"));
        var auditRow = Assert.Single(languageLines, l => l.StartsWith("| **Audit File**", StringComparison.Ordinal));
        Assert.Contains("every generation mode", auditRow);
        Assert.Contains("Standard Mode", auditRow);
        Assert.Contains("Loadfile-Only Mode", auditRow);
        Assert.Contains("Production Set", auditRow);

        var requirement = Assert.Single(File.ReadAllLines(Path.Combine(RepoRoot, "Requirements.md")),
            l => l.StartsWith("- **REQ-090**:", StringComparison.Ordinal));
        Assert.Contains("Every generation mode", requirement);
        Assert.Contains("When the Chaos Engine injects anomalies", requirement);
        Assert.Contains("chaos anomaly manifest", requirement);

        var auditWriterSource = File.ReadAllText(Path.Combine(RepoRoot, "src", "LoadFileAuditWriter.cs"));
        Assert.DoesNotContain("for loadfile-only mode", auditWriterSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("only in loadfile-only mode", auditWriterSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every generation mode", auditWriterSource);

        var advancedGuide = File.ReadAllText(Path.Combine(RepoRoot, "docs", "advanced-guide.md"));
        Assert.DoesNotContain("Loadfile-Only Audit File", advancedGuide);
        Assert.Contains("Written with every generated Load File in every generation mode", advancedGuide);
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

    [Fact]
    public void RunTestsBat_HasExplicitEntryPointBeforeHelperSubroutines()
    {
        var runTestsBatPath = Path.Combine(RepoRoot, "tests", "run-tests.bat");
        var lines = File.ReadAllLines(runTestsBatPath);

        int firstLabelIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith(':') && !trimmed.StartsWith("::", StringComparison.Ordinal))
            {
                firstLabelIndex = i;
                break;
            }
        }

        Assert.True(firstLabelIndex > 0, "run-tests.bat must have at least one subroutine label.");

        bool hasGotoMain = false;
        for (int i = 0; i < firstLabelIndex; i++)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"^\s*goto\s+:?main\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                hasGotoMain = true;
                break;
            }
        }

        Assert.True(hasGotoMain, $"run-tests.bat must contain 'goto :main' before label '{lines[firstLabelIndex].Trim()}' at line {firstLabelIndex + 1} (#1405).");

        int mainLabelIndex = -1;
        int endOfTestsIndex = -1;
        int firstCaseIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^:main\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                if (mainLabelIndex < 0)
                {
                    mainLabelIndex = i;
                }
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^:end_of_tests\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                if (endOfTestsIndex < 0)
                {
                    endOfTestsIndex = i;
                }
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"call\s+:run_test_case\s+""Test Case 1:", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                if (firstCaseIndex < 0)
                {
                    firstCaseIndex = i;
                }
            }
        }

        Assert.True(mainLabelIndex >= 0, "run-tests.bat must contain a ':main' label.");
        Assert.True(endOfTestsIndex > mainLabelIndex, "run-tests.bat must contain ':end_of_tests' after ':main'.");
        Assert.True(firstCaseIndex >= 0, "run-tests.bat must contain 'Test Case 1'.");
        Assert.True(mainLabelIndex < firstCaseIndex, "':main' label must appear before Test Case 1 in run-tests.bat.");

        // Check that no helper subroutines terminating with goto :eof or exit /b are placed between :main and :end_of_tests
        var terminatingSubroutines = new List<string>();
        for (int i = mainLabelIndex + 1; i < endOfTestsIndex; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith(':') && !trimmed.StartsWith("::", StringComparison.Ordinal))
            {
                for (int j = i + 1; j < endOfTestsIndex; j++)
                {
                    var forwardTrimmed = lines[j].TrimStart();
                    if (forwardTrimmed.StartsWith(':') && !forwardTrimmed.StartsWith("::", StringComparison.Ordinal))
                    {
                        break;
                    }
                    if (System.Text.RegularExpressions.Regex.IsMatch(forwardTrimmed, @"^(goto\s*:?eof|exit\s*/b|exit\s+[0-9]+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                    {
                        terminatingSubroutines.Add(trimmed);
                        break;
                    }
                }
            }
        }

        Assert.Empty(terminatingSubroutines);
    }
}

