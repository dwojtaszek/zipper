using Xunit;
using Zipper.ArchiveTests;
using Zipper.Cli;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class HelpTextGeneratorTests
{
    [Fact]
    public void Show_DisplaysUsageInformation()
    {
        var (_, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(HelpTextGenerator.Show);

        Assert.Contains("Error: Missing required arguments.", stderr, StringComparison.Ordinal);
        Assert.Contains("Usage:", stderr, StringComparison.Ordinal);
        Assert.Contains("--type <pdf|jpg|tiff|eml|docx|xlsx>", stderr, StringComparison.Ordinal);
        Assert.Contains("--count <number>", stderr, StringComparison.Ordinal);
        Assert.Contains("--output-path <path>", stderr, StringComparison.Ordinal);
        Assert.Contains("Optional Arguments:", stderr, StringComparison.Ordinal);
        Assert.Contains("Required Arguments:", stderr, StringComparison.Ordinal);
        Assert.Contains("Load File Options:", stderr, StringComparison.Ordinal);
        Assert.Contains("Loadfile-Only Options:", stderr, StringComparison.Ordinal);
        Assert.Contains("Chaos Engine Options:", stderr, StringComparison.Ordinal);
        var normalizedHelpLines = stderr
            .Split(Environment.NewLine)
            .Select(line => string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToArray();
        Assert.Contains("--chaos-mode Enable Chaos Engine processing", normalizedHelpLines);
        Assert.Contains("--chaos-amount <value> Target count: percentage (1%) or exact (500)", normalizedHelpLines);
        Assert.Contains("--chaos-scenario <name> Predefined scenario; conflicts with --chaos-types; adversarial-values emits valid DAT values", normalizedHelpLines);
        Assert.Contains(string.Join(", ", ArchiveTestCatalog.SupportedSuites), stderr, StringComparison.Ordinal);
        Assert.Contains("Production Set Options:", stderr, StringComparison.Ordinal);
        Assert.Contains("--redacted-production", stderr, StringComparison.Ordinal);
        Assert.Contains("--withheld-native-policy", stderr, StringComparison.Ordinal);
        Assert.Contains(
            "  --production-id <string> Configurable production ID (requires --production-set; non-empty; supports lists, defaults to auto-incrementing/timestamp), max 250 UTF-8 bytes per generated element, single safe path segment per element",
            stderr,
            StringComparison.Ordinal);
        Assert.Contains("Bates Numbering:", stderr, StringComparison.Ordinal);
        Assert.Contains(
            "  --bates-prefix <string>  Bates Number prefix, max 200 UTF-8 bytes per list element (e.g., CLIENT001)",
            stderr,
            StringComparison.Ordinal);
        Assert.Contains("TIFF Options:", stderr, StringComparison.Ordinal);
        Assert.Contains("Column Profile Options:", stderr, StringComparison.Ordinal);
        Assert.Contains("Utility Options:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_MarksDeflate64Bzip2AsNotYetSupported()
    {
        var (_, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(HelpTextGenerator.Show);

        var compressionLine = stderr
            .Split('\n')
            .FirstOrDefault(line => line.IndexOf("--compression", StringComparison.Ordinal) >= 0);

        Assert.NotNull(compressionLine);
        Assert.True(compressionLine.IndexOf("not yet supported", StringComparison.OrdinalIgnoreCase) >= 0,
            $"Expected deflate64/bzip2 marked not yet supported in help compression line: {compressionLine}");
    }
}
