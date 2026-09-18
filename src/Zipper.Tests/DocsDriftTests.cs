using Xunit;

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
            .FirstOrDefault(l => l.IndexOf("--load-file-format", StringComparison.Ordinal) >= 0
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

        var compressionLine = lines
            .FirstOrDefault(l => l.IndexOf("--compression", StringComparison.Ordinal) >= 0
                              && l.IndexOf("store", StringComparison.Ordinal) >= 0);

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
}
