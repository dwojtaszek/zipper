using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

public class AdvancedGuideRecipeTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string AdvancedGuidePath = Path.Combine(RepoRoot, "docs", "advanced-guide.md");

    private readonly string tempDir;

    public AdvancedGuideRecipeTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), "TestGuide_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            try
            {
                Directory.Delete(this.tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                && File.Exists(Path.Combine(dir.FullName, "Requirements.md"))
                && File.Exists(Path.Combine(dir.FullName, "docs", "advanced-guide.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate repo root containing docs/advanced-guide.md.");
    }

    private static string ExtractJsonFence(string markdown, string sectionHeader)
    {
        var headerIndex = markdown.IndexOf(sectionHeader, StringComparison.OrdinalIgnoreCase);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException($"Section '{sectionHeader}' not found in documentation.");
        }

        var jsonFenceStart = markdown.IndexOf("```json", headerIndex, StringComparison.OrdinalIgnoreCase);
        if (jsonFenceStart < 0)
        {
            throw new InvalidOperationException($"No ```json fence found after section '{sectionHeader}'.");
        }

        var contentStart = markdown.IndexOf('\n', jsonFenceStart) + 1;
        var jsonFenceEnd = markdown.IndexOf("```", contentStart, StringComparison.Ordinal);
        if (jsonFenceEnd < 0)
        {
            throw new InvalidOperationException($"Unclosed ```json fence after section '{sectionHeader}'.");
        }

        return markdown[contentStart..jsonFenceEnd].Trim();
    }

    private static string ExtractBashCommand(string markdown, string sectionHeader)
    {
        var headerIndex = markdown.IndexOf(sectionHeader, StringComparison.OrdinalIgnoreCase);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException($"Section '{sectionHeader}' not found in documentation.");
        }

        var bashFenceStart = markdown.IndexOf("```bash", headerIndex, StringComparison.OrdinalIgnoreCase);
        if (bashFenceStart < 0)
        {
            throw new InvalidOperationException($"No ```bash fence found after section '{sectionHeader}'.");
        }

        var contentStart = markdown.IndexOf('\n', bashFenceStart) + 1;
        var bashFenceEnd = markdown.IndexOf("```", contentStart, StringComparison.Ordinal);
        if (bashFenceEnd < 0)
        {
            throw new InvalidOperationException($"Unclosed ```bash fence after section '{sectionHeader}'.");
        }

        return markdown[contentStart..bashFenceEnd].Trim();
    }

    private static string[] TokenizeCommandLine(string commandLine)
    {
        var tokens = new List<string>();
        var pattern = @"[\""].+?[\""]|[^ ]+";
        var matches = Regex.Matches(commandLine, pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
        foreach (Match match in matches)
        {
            var val = match.Value;
            if (val.StartsWith('\"') && val.EndsWith('\"') && val.Length >= 2)
            {
                val = val[1..^1];
            }

            tokens.Add(val);
        }

        return tokens.ToArray();
    }

    [Fact]
    public void DocumentedCustomColumnProfile_ExactExtractedJson_LoadsAndValidatesSuccessfully()
    {
        var markdown = File.ReadAllText(AdvancedGuidePath);
        var json = ExtractJsonFence(markdown, "Example Custom Profile");

        var profilePath = Path.Combine(this.tempDir, "custom-profile.json");
        File.WriteAllText(profilePath, json);

        var profile = ColumnProfileLoader.LoadFromFile(profilePath, this.tempDir);
        Assert.NotNull(profile);
        Assert.Equal("custom-ediscovery", profile.Name);
        Assert.Contains(profile.Columns, c => c.Type.Equals("identifier", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.Columns, c => c.Type.Equals("text", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.Columns, c => c.Type.Equals("date", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.Columns, c => c.Type.Equals("coded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.Columns, c => c.Type.Equals("number", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.Columns, c => c.Type.Equals("longtext", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DocumentedCustomColumnProfile_GeneratesBoundedDataWithExpectedFieldShapes()
    {
        var markdown = File.ReadAllText(AdvancedGuidePath);
        var json = ExtractJsonFence(markdown, "Example Custom Profile");

        var profilePath = Path.Combine(this.tempDir, "custom-profile.json");
        File.WriteAllText(profilePath, json);

        var outputDir = Path.Combine(this.tempDir, "custom_output");
        Directory.CreateDirectory(outputDir);

        var commandLine = ExtractBashCommand(markdown, "### Usage:");
        var tokens = TokenizeCommandLine(commandLine);
        var argsList = new List<string>();
        const int recordCount = 10;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (i == 0 && tokens[i].Equals("zipper", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tokens[i].Equals("--count", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--count");
                argsList.Add(recordCount.ToString(CultureInfo.InvariantCulture));
                i++;
                continue;
            }

            if (tokens[i].Equals("--output-path", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--output-path");
                argsList.Add(outputDir);
                i++;
                continue;
            }

            if (tokens[i].Equals("--column-profile", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--column-profile");
                argsList.Add(profilePath);
                i++;
                continue;
            }

            argsList.Add(tokens[i]);
        }

        argsList.Add("--seed");
        argsList.Add("42");

        var exitCode = await Program.Main(argsList.ToArray());
        Assert.Equal(0, exitCode);

        var datFile = Directory.GetFiles(outputDir, "*.dat").FirstOrDefault();
        Assert.NotNull(datFile);

        var lines = (await File.ReadAllLinesAsync(datFile))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        Assert.Equal(recordCount + 1, lines.Count); // Header + 10 records

        var headerFields = lines[0].Split('\x14').Select(f => f.Trim('\xFE', ' ')).ToList();
        var controlNumIdx = headerFields.IndexOf("CONTROL_NUMBER");
        var custodianIdx = headerFields.IndexOf("CUSTODIAN");
        var dateIdx = headerFields.IndexOf("DOCUMENT_DATE");
        var confIdx = headerFields.IndexOf("CONFIDENTIALITY");
        var sizeIdx = headerFields.IndexOf("FILE_SIZE_BYTES");
        var notesIdx = headerFields.IndexOf("REVIEW_NOTES");

        Assert.True(controlNumIdx >= 0, "Missing CONTROL_NUMBER header");
        Assert.True(custodianIdx >= 0, "Missing CUSTODIAN header");
        Assert.True(dateIdx >= 0, "Missing DOCUMENT_DATE header");
        Assert.True(confIdx >= 0, "Missing CONFIDENTIALITY header");
        Assert.True(sizeIdx >= 0, "Missing FILE_SIZE_BYTES header");
        Assert.True(notesIdx >= 0, "Missing REVIEW_NOTES header");

        var expectedConfidentiality = new HashSet<string>(StringComparer.Ordinal)
        {
            "Public", "Confidential", "Highly Confidential", "Restricted",
        };

        var minDate = new DateTime(2020, 1, 1);
        var maxDate = new DateTime(2026, 12, 31);

        for (int i = 1; i <= recordCount; i++)
        {
            var fields = lines[i].Split('\x14').Select(f => f.Trim('\xFE')).ToList();

            // Identifier shape: DOC followed by 8 digits
            var controlNumber = fields[controlNumIdx];
            Assert.Matches(@"^DOC\d{8}$", controlNumber);

            // Custodian: Custodian_ prefix from data source
            var custodian = fields[custodianIdx];
            Assert.Matches(@"^Custodian_\d+$", custodian);

            // Date bounds: 2020-01-01 to 2026-12-31
            var dateStr = fields[dateIdx];
            Assert.True(DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date), $"Invalid date: '{dateStr}'");
            Assert.True(date >= minDate && date <= maxDate, $"Date '{date}' outside [{minDate:yyyy-MM-dd}, {maxDate:yyyy-MM-dd}]");

            // Coded membership
            var conf = fields[confIdx];
            Assert.Contains(conf, expectedConfidentiality);

            // Number bounds: 1024 to 10485760
            var sizeStr = fields[sizeIdx];
            Assert.True(int.TryParse(sizeStr, CultureInfo.InvariantCulture, out var size), $"Invalid number: '{sizeStr}'");
            Assert.InRange(size, 1024, 10485760);

            // Longtext non-empty
            var notes = fields[notesIdx];
            Assert.False(string.IsNullOrWhiteSpace(notes), "REVIEW_NOTES should not be empty");
        }
    }

    [Fact]
    public async Task RecipeD_DocumentedCommand_IsSupportedFileTypeMixWithoutColumnProfile()
    {
        var markdown = File.ReadAllText(AdvancedGuidePath);
        var commandLine = ExtractBashCommand(markdown, "### Recipe D");

        Assert.DoesNotContain("--column-profile", commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--types", commandLine, StringComparison.OrdinalIgnoreCase);

        var tokens = TokenizeCommandLine(commandLine);
        var argsList = new List<string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (i == 0 && tokens[i].Equals("zipper", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tokens[i].Equals("--count", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--count");
                argsList.Add("10"); // Bounded count for testing
                i++;
                continue;
            }

            if (tokens[i].Equals("--output-path", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--output-path");
                argsList.Add(Path.Combine(this.tempDir, "mixed_archive"));
                i++;
                continue;
            }

            argsList.Add(tokens[i]);
        }

        var exitCode = await Program.Main(argsList.ToArray());
        Assert.Equal(0, exitCode);

        var mixedOutputDir = Path.Combine(this.tempDir, "mixed_archive");
        var zipFiles = Directory.GetFiles(mixedOutputDir, "*.zip");
        Assert.Single(zipFiles);
        var datFiles = Directory.GetFiles(mixedOutputDir, "*.dat");
        Assert.Single(datFiles);
    }

    [Fact]
    public async Task RecipeE_DocumentedSingleFileTypeProfile_ExecutesSuccessfully()
    {
        var markdown = File.ReadAllText(AdvancedGuidePath);
        var commandLine = ExtractBashCommand(markdown, "### Recipe E");

        Assert.Contains("--column-profile", commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--type", commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--types", commandLine, StringComparison.OrdinalIgnoreCase);

        var tokens = TokenizeCommandLine(commandLine);
        var argsList = new List<string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (i == 0 && tokens[i].Equals("zipper", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tokens[i].Equals("--count", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--count");
                argsList.Add("5"); // Bounded count for testing
                i++;
                continue;
            }

            if (tokens[i].Equals("--output-path", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--output-path");
                argsList.Add(Path.Combine(this.tempDir, "litigation_archive"));
                i++;
                continue;
            }

            argsList.Add(tokens[i]);
        }

        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        int exitCode;
        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            exitCode = await Program.Main(argsList.ToArray());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var output = outWriter.ToString() + errWriter.ToString();
        Assert.True(exitCode == 0, $"Program.Main failed with exit code {exitCode}. Output: {output}. Args: {string.Join(" ", argsList)}");

        var profileOutputDir = Path.Combine(this.tempDir, "litigation_archive");
        var zipFiles = Directory.GetFiles(profileOutputDir, "*.zip");
        Assert.Single(zipFiles);
        var datFiles = Directory.GetFiles(profileOutputDir, "*.dat");
        Assert.Single(datFiles);
    }

    [Fact]
    public void TouchedRecipes_TerminologyNormalizedToUbiquitousLanguage()
    {
        var markdown = File.ReadAllText(AdvancedGuidePath);

        // Recipe C: Normalized to Email and Attachment
        var recipeCIndex = markdown.IndexOf("### Recipe C", StringComparison.OrdinalIgnoreCase);
        var recipeDIndex = markdown.IndexOf("### Recipe D", StringComparison.OrdinalIgnoreCase);
        var recipeEIndex = markdown.IndexOf("### Recipe E", StringComparison.OrdinalIgnoreCase);
        var section4Index = markdown.IndexOf("## 4. Custom Column Profile", StringComparison.OrdinalIgnoreCase);

        Assert.True(recipeCIndex >= 0, "Recipe C not found");
        Assert.True(recipeDIndex >= 0, "Recipe D not found");
        Assert.True(recipeEIndex >= 0, "Recipe E not found");
        Assert.True(section4Index >= 0, "Section 4 not found");

        var recipeCText = markdown[recipeCIndex..recipeDIndex];
        Assert.DoesNotContain("E-Mail", recipeCText, StringComparison.Ordinal);
        Assert.Contains("Email", recipeCText, StringComparison.Ordinal);
        Assert.Contains("Attachment", recipeCText, StringComparison.Ordinal);

        var recipeDText = markdown[recipeDIndex..recipeEIndex];
        Assert.DoesNotContain("multi-file-type archive", recipeDText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("E-Mail", recipeDText, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata profile", recipeDText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("File Type Mix", recipeDText, StringComparison.Ordinal);
        Assert.Contains("Archive", recipeDText, StringComparison.Ordinal);
        Assert.Contains("Native File", recipeDText, StringComparison.Ordinal);
        Assert.Contains("Email", recipeDText, StringComparison.Ordinal);
        Assert.Contains("Metadata", recipeDText, StringComparison.Ordinal);

        var recipeEText = markdown[recipeEIndex..section4Index];
        Assert.Contains("Column Profile", recipeEText, StringComparison.Ordinal);
        Assert.Contains("Archive", recipeEText, StringComparison.Ordinal);
        Assert.Contains("Native File", recipeEText, StringComparison.Ordinal);
        Assert.Contains("Metadata", recipeEText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Section5_DocumentedColumnProfileExample_ExecutesSuccessfully()
    {
        var markdown = File.ReadAllText(AdvancedGuidePath);
        var commandLine = ExtractBashCommand(markdown, "### Column Profiles");

        Assert.Contains("--column-profile litigation", commandLine, StringComparison.OrdinalIgnoreCase);

        var tokens = TokenizeCommandLine(commandLine);
        var argsList = new List<string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (i == 0 && tokens[i].Equals("zipper", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tokens[i].Equals("--count", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--count");
                argsList.Add("5");
                i++;
                continue;
            }

            if (tokens[i].Equals("--output-path", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                argsList.Add("--output-path");
                argsList.Add(Path.Combine(this.tempDir, "sec5_litigation"));
                i++;
                continue;
            }

            argsList.Add(tokens[i]);
        }

        var exitCode = await Program.Main(argsList.ToArray());
        Assert.Equal(0, exitCode);

        var outputDir = Path.Combine(this.tempDir, "sec5_litigation");
        var zipFiles = Directory.GetFiles(outputDir, "*.zip");
        Assert.Single(zipFiles);
        var datFiles = Directory.GetFiles(outputDir, "*.dat");
        Assert.Single(datFiles);
    }
}
