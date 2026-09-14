using Xunit;
using Zipper.Profiles;

namespace Zipper.Tests;

/// <summary>
/// Tests for the DataGenerator class.
/// </summary>
public class DataGeneratorTests
{
    /// <summary>
    /// Test that DataGenerator generates correct number of columns.
    /// </summary>
    [Fact]
    public void GenerateRow_ReturnsCorrectNumberOfColumns()
    {
        var profile = BuiltInProfiles.Minimal;
        var generator = new DataGenerator(profile);
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/file001.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        Assert.Equal(profile.Columns.Count, row.Count);
    }

    /// <summary>
    /// Test that identifier column generates correct format.
    /// </summary>
    [Fact]
    public void GenerateRow_IdentifierColumn_HasCorrectFormat()
    {
        var profile = BuiltInProfiles.Minimal;
        var generator = new DataGenerator(profile);
        var workItem = new FileWorkItem { Index = 42, FilePathInZip = "Folder001/file042.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        Assert.Equal("DOC00000042", row["DOCID"]);
    }

    /// <summary>
    /// Test that FILEPATH column contains actual path.
    /// </summary>
    [Fact]
    public void GenerateRow_FilePathColumn_ContainsPath()
    {
        var profile = BuiltInProfiles.Minimal;
        var generator = new DataGenerator(profile);
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/document.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        Assert.Equal("Folder001/document.pdf", row["FILEPATH"]);
    }

    /// <summary>
    /// Test that seed produces reproducible output.
    /// </summary>
    [Fact]
    public void GenerateRow_WithSeed_ProducesReproducibleOutput()
    {
        var profile = BuiltInProfiles.Standard;
        var generator1 = new DataGenerator(profile, seed: 12345);
        var generator2 = new DataGenerator(profile, seed: 12345);

        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/file001.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row1 = generator1.GenerateRow(workItem, fileData);
        var row2 = generator2.GenerateRow(workItem, fileData);

        Assert.Equal(row1["CUSTODIAN"], row2["CUSTODIAN"]);
        Assert.Equal(row1["DATECREATED"], row2["DATECREATED"]);
    }

    /// <summary>
    /// Test that GetColumnNames returns all column names.
    /// </summary>
    [Fact]
    public void GetColumnNames_ReturnsAllColumns()
    {
        var profile = BuiltInProfiles.Minimal;
        var generator = new DataGenerator(profile);

        var columnNames = generator.GetColumnNames().ToList();

        Assert.Equal(profile.Columns.Count, columnNames.Count);
        Assert.Contains("DOCID", columnNames);
        Assert.Contains("FILEPATH", columnNames);
    }

    /// <summary>
    /// Test that boolean columns generate Y or N values.
    /// </summary>
    [Fact]
    public void GenerateRow_BooleanColumn_GeneratesYOrN()
    {
        // Use Litigation profile which has actual boolean columns
        var profile = BuiltInProfiles.Litigation;
        var generator = new DataGenerator(profile, seed: 12345);
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/file001.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        // ISRESPONSIVE is a boolean column with Format = "YN"
        var responsiveValue = row["ISRESPONSIVE"];
        Assert.True(
string.Equals(responsiveValue, "Y", StringComparison.Ordinal) || string.Equals(responsiveValue, "N", StringComparison.Ordinal) || string.IsNullOrEmpty(responsiveValue),
            $"Expected Y, N, or empty but got {responsiveValue}");
    }

    /// <summary>
    /// Test that date columns generate valid date format.
    /// </summary>
    [Fact]
    public void GenerateRow_DateColumn_GeneratesValidDateFormat()
    {
        var profile = BuiltInProfiles.Standard;
        var generator = new DataGenerator(profile, seed: 12345);
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/file001.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        var dateValue = row["DATECREATED"];
        if (!string.IsNullOrEmpty(dateValue))
        {
            Assert.True(
                DateTime.TryParse(dateValue, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _),
                $"Expected valid date but got {dateValue}");
        }
    }

    /// <summary>
    /// Test that email columns generate valid email format.
    /// </summary>
    [Fact]
    public void GenerateRow_EmailColumn_GeneratesValidEmailFormat()
    {
        var profile = BuiltInProfiles.Standard;
        var generator = new DataGenerator(profile, seed: 12345);
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/file001.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        var emailValue = row["EMAILFROM"];
        if (!string.IsNullOrEmpty(emailValue))
        {
            Assert.Contains("@", emailValue, StringComparison.Ordinal);
            Assert.Contains(".", emailValue, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Test that required columns are never empty.
    /// </summary>
    [Fact]
    public void GenerateRow_RequiredColumns_AreNeverEmpty()
    {
        var profile = BuiltInProfiles.Minimal;
        var generator = new DataGenerator(profile, seed: 12345);

        for (int i = 1; i <= 100; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder001/file{i:D3}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            var requiredColumns = profile.Columns.Where(c => c.Required);
            foreach (var column in requiredColumns)
            {
                Assert.False(string.IsNullOrEmpty(row[column.Name]), $"Required column {column.Name} was empty on iteration {i}");
            }
        }
    }

    /// <summary>
    /// Test that multi-value columns use the correct delimiter.
    /// </summary>
    [Fact]
    public void GenerateRow_MultiValueColumns_UseCorrectDelimiter()
    {
        var profile = BuiltInProfiles.Standard;
        var generator = new DataGenerator(profile, seed: 42);
        var delimiter = profile.Settings.MultiValueDelimiter;

        // Generate multiple rows to find one with multi-value
        for (int i = 1; i <= 100; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder001/file{i:D3}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            var emailToValue = row["EMAILTO"];
            if (!string.IsNullOrEmpty(emailToValue) && emailToValue.Contains(delimiter, StringComparison.Ordinal))
            {
                Assert.Contains(delimiter, emailToValue, StringComparison.Ordinal);
                var parts = emailToValue.Split(delimiter);
                Assert.True(parts.Length >= 2);
                return;
            }
        }

        Assert.Fail("No multi-value column found in 100 iterations with seed 42");
    }

    /// <summary>
    /// Test that number columns generate valid numbers.
    /// </summary>
    [Fact]
    public void GenerateRow_NumberColumn_GeneratesValidNumber()
    {
        var profile = BuiltInProfiles.Standard;
        var generator = new DataGenerator(profile, seed: 12345);
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/file001.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        var fileSizeValue = row["FILESIZE"];
        Assert.True(
            long.TryParse(fileSizeValue, System.Globalization.CultureInfo.InvariantCulture, out var size),
            $"Expected valid number but got {fileSizeValue}");
        Assert.True(size >= 0, "File size should be non-negative");
    }

    /// <summary>
    /// Test that a custom profile exercising every column kind produces correct values.
    /// </summary>
    [Fact]
    public void CustomProfile_WithEveryColumnKind_AllColumnsWithinSpec()
    {
        var profile = new ColumnProfile
        {
            Name = "test-every-kind",
            DataSources = new System.Collections.Generic.Dictionary<string, DataSourceConfig>
(StringComparer.Ordinal)
            {
                ["statusValues"] = new DataSourceConfig { Values = ["Active", "Inactive", "Pending", "Closed", "Archived"] },
                ["categoryValues"] = new DataSourceConfig { Values = ["CategoryA", "CategoryB", "CategoryC", "CategoryD"], Weights = [40, 30, 20, 10] },
            },
            Columns = new System.Collections.Generic.List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "TEXTFIELD", Type = "text", Required = true },
                new() { Name = "LONGTEXTFIELD", Type = "longtext", Required = true },
                new() { Name = "DATEFIELD", Type = "date", Required = true, DateRange = new DateRangeConfig { Min = "2018-01-01", Max = "2024-12-31" } },
                new() { Name = "DATETIMEFIELD", Type = "datetime", Required = true, DateRange = new DateRangeConfig { Min = "2018-01-01", Max = "2024-12-31" } },
                new() { Name = "NUMBERFIELD", Type = "number", Required = true, Range = new RangeConfig { Min = 0, Max = 10000 } },
                new() { Name = "BOOLEANFIELD", Type = "boolean", Required = true, TruePercentage = 60, Format = "YN" },
                new() { Name = "CODEDFIELD", Type = "coded", Required = true, DataSource = "statusValues" },
                new() { Name = "EMAILFIELD", Type = "email", Required = true },
                new() { Name = "EMAILMULTI", Type = "email", Required = true, MultiValue = true, MultiValueCount = new RangeConfig { Min = 2, Max = 4 } },
            },
        };
        var generator = new DataGenerator(profile, seed: 42);
        var validCoded = new HashSet<string>(StringComparer.Ordinal) { "Active", "Inactive", "Pending", "Closed", "Archived" };
        var validBool = new HashSet<string>(StringComparer.Ordinal) { "Y", "N" };

        for (int i = 1; i <= 200; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"NATIVES/001/DOC{i:D8}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            // identifier
            Assert.Matches(@"^DOC[0-9]+$", row["DOCID"]);

            // date: yyyy-MM-dd format
            if (!string.IsNullOrEmpty(row["DATEFIELD"]))
            {
                Assert.True(
                    DateTime.TryParseExact(row["DATEFIELD"], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _),
                    $"DATEFIELD '{row["DATEFIELD"]}' is not yyyy-MM-dd");
            }

            // datetime: ISO 8601 with time component
            if (!string.IsNullOrEmpty(row["DATETIMEFIELD"]))
            {
                Assert.Contains("T", row["DATETIMEFIELD"], StringComparison.Ordinal);
            }

            // number: integer in valid range
            if (!string.IsNullOrEmpty(row["NUMBERFIELD"]))
            {
                Assert.True(int.TryParse(row["NUMBERFIELD"], System.Globalization.CultureInfo.InvariantCulture, out var numVal), $"NUMBERFIELD is not an integer: {row["NUMBERFIELD"]}");
                Assert.InRange(numVal, 0, 10000);
            }

            // boolean: Y or N
            if (!string.IsNullOrEmpty(row["BOOLEANFIELD"]))
            {
                Assert.Contains(row["BOOLEANFIELD"], validBool);
            }

            // coded: from declared set
            if (!string.IsNullOrEmpty(row["CODEDFIELD"]))
            {
                Assert.Contains(row["CODEDFIELD"], validCoded);
            }

            // email: matches pattern
            if (!string.IsNullOrEmpty(row["EMAILFIELD"]))
            {
                Assert.Matches(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$", row["EMAILFIELD"]);
            }
        }
    }

    /// <summary>
    /// Test that EmptyPercentage produces the configured empty rate within statistical tolerance.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void EmptyPercentage_Observed_MatchesConfigured_WithinTolerance(int emptyPct)
    {
        var profile = new ColumnProfile
        {
            Name = $"test-empty-{emptyPct}",
            DataSources = new System.Collections.Generic.Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            Columns = new System.Collections.Generic.List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "TEXTFIELD", Type = "text", EmptyPercentage = emptyPct },
            },
        };

        var generator = new DataGenerator(profile, seed: 42);
        int sampleSize = 5000;
        int emptyCount = 0;

        for (int i = 1; i <= sampleSize; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"NATIVES/001/DOC{i:D8}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);
            if (string.IsNullOrEmpty(row["TEXTFIELD"]))
            {
                emptyCount++;
            }
        }

        if (emptyPct == 0)
        {
            Assert.Equal(0, emptyCount);
        }
        else if (emptyPct == 100)
        {
            Assert.Equal(sampleSize, emptyCount);
        }
        else
        {
            // Allow ±5% absolute tolerance for random variation
            double observed = (double)emptyCount / sampleSize * 100;
            Assert.InRange(observed, emptyPct - 5.0, emptyPct + 5.0);
        }
    }

    /// <summary>
    /// Test that when weight count exceeds values count, the distribution is proportional to the values' weights.
    /// </summary>
    [Fact]
    public void GenerateRow_WeightsCountExceedingValuesCount_ShouldDistributeProportionally()
    {
        // Arrange
        var profile = new ColumnProfile
        {
            Name = "test-weighted-bias",
            DataSources = new System.Collections.Generic.Dictionary<string, DataSourceConfig>
(StringComparer.Ordinal)
            {
                ["weightedDS"] = new DataSourceConfig
                {
                    Values = new List<string> { "A", "B" },
                    Weights = new List<int> { 1, 1, 100 }, // 3 weights for 2 values
                    Distribution = "weighted"
                }
            },
            Columns = new System.Collections.Generic.List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "WEIGHTEDFIELD", Type = "text", Required = true, DataSource = "weightedDS" }
            }
        };

        // Note: ColumnProfileLoader.Validate is bypassed by instantiating DataGenerator directly.
        var generator = new DataGenerator(profile, seed: 12345);
        int sampleSize = 1000;
        int aCount = 0;
        int bCount = 0;

        for (int i = 1; i <= sampleSize; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            var value = row["WEIGHTEDFIELD"];
            if (string.Equals(value, "A", StringComparison.Ordinal))
            {
                aCount++;
            }
            else if (string.Equals(value, "B", StringComparison.Ordinal))
            {
                bCount++;
            }
        }

        // With the fix, we ignore the extra weight (100) and only use the first two weights (1, 1).
        // This should result in a 50/50 proportional distribution between A and B, within a standard tolerance.
        double aPercent = (double)aCount / sampleSize * 100;
        double bPercent = (double)bCount / sampleSize * 100;
        Assert.InRange(aPercent, 45.0, 55.0);
        Assert.InRange(bPercent, 45.0, 55.0);
    }

    /// <summary>
    /// Test that custodianCountOverride replaces the profile's custodians data source Count,
    /// producing only values within the override range.
    /// </summary>
    [Fact]
    public void GenerateRow_WithCustodianCountOverride_LimitsDistinctCustodians()
    {
        // Standard profile has 25 custodians by default.
        // We override to 3 — so every generated custodian value must be within Custodian_1..Custodian_3.
        // Use 500 rows (seeded) to ensure all 3 values appear given pareto distribution.
        var profile = BuiltInProfiles.Standard;
        var generator = new DataGenerator(profile, seed: 42, custodianCountOverride: 3);
        var validValues = new HashSet<string>(StringComparer.Ordinal) { "Custodian_1", "Custodian_2", "Custodian_3" };

        var custodianValues = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 1; i <= 500; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"NATIVES/001/DOC{i:D8}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            if (!string.IsNullOrEmpty(row["CUSTODIAN"]))
            {
                custodianValues.Add(row["CUSTODIAN"]);
            }
        }

        // Upper bound: no value outside the 3 allowed
        Assert.True(
            custodianValues.Count <= 3,
            $"Expected at most 3 distinct custodians but got {custodianValues.Count}: {string.Join(", ", custodianValues)}");
        foreach (var v in custodianValues)
        {
            Assert.Contains(v, validValues);
        }

        // Lower bound: at least Custodian_1 must appear (it gets 80%+ under pareto)
        Assert.Contains("Custodian_1", custodianValues);
    }

    /// <summary>
    /// Test that custodianCountOverride is a no-op for profiles that have no "custodians" data source.
    /// </summary>
    [Fact]
    public void GenerateRow_WithCustodianCountOverride_NoCustodiansDataSource_NoOp()
    {
        // Minimal profile has a custodians source; build a custom profile that does NOT
        var profile = new ColumnProfile
        {
            Name = "no-custodians",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal),
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
            },
        };

        // Should not throw; the override should be silently ignored
        var generator = new DataGenerator(profile, seed: 42, custodianCountOverride: 5);
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "NATIVES/001/DOC00000001.pdf" };
        var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);
        Assert.Single(row); // Only DOCID
    }

    /// <summary>
    /// Test that custodianCountOverride truncates a static Values-based custodians data source.
    /// </summary>
    [Fact]
    public void GenerateRow_WithCustodianCountOverride_ValuesBasedSource_TruncatesToOverrideCount()
    {
        var profile = new ColumnProfile
        {
            Name = "values-custodians",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>
(StringComparer.Ordinal)
            {
                ["custodians"] = new DataSourceConfig
                {
                    Values = new List<string> { "Alice", "Bob", "Carol", "Dave", "Eve" },
                    Distribution = "uniform",
                },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "CUSTODIAN", Type = "coded", DataSource = "custodians", EmptyPercentage = 0 },
            },
        };

        // Override to 2: only "Alice" and "Bob" should ever appear
        var generator = new DataGenerator(profile, seed: 42, custodianCountOverride: 2);
        var custodianValues = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 1; i <= 200; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"NATIVES/001/DOC{i:D8}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);
            custodianValues.Add(row["CUSTODIAN"]);
        }

        Assert.True(
            custodianValues.Count <= 2,
            $"Expected at most 2 custodians (Alice, Bob) but got {custodianValues.Count}: {string.Join(", ", custodianValues)}");
        foreach (var v in custodianValues)
        {
            Assert.True(string.Equals(v, "Alice", StringComparison.Ordinal) || string.Equals(v, "Bob", StringComparison.Ordinal), $"Unexpected custodian value: {v}");
        }
    }

    /// <summary>
    /// Regression test: when a Values-based custodian source also has Weights,
    /// both must be truncated to effectiveCount so PrecomputeIndices works correctly.
    /// Before the fix, the full Weights list was kept, causing index misalignment.
    /// </summary>
    [Fact]
    public void GenerateRow_WithCustodianCountOverride_WeightedValuesBasedSource_TruncatesBothValuesAndWeights()
    {
        var profile = new ColumnProfile
        {
            Name = "weighted-values-custodians",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>
(StringComparer.Ordinal)
            {
                ["custodians"] = new DataSourceConfig
                {
                    Values = new List<string> { "Alice", "Bob", "Carol", "Dave", "Eve" },
                    Weights = new List<int> { 50, 30, 10, 5, 5 },
                    Distribution = "weighted",
                },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "CUSTODIAN", Type = "coded", DataSource = "custodians", EmptyPercentage = 0 },
            },
        };

        // Override to 2: only "Alice" (weight 50) and "Bob" (weight 30) should appear.
        // If Weights was NOT truncated, PrecomputeIndices would sum all 5 weights (100)
        // but only 2 values exist, causing index-out-of-range fallback to the last value.
        var generator = new DataGenerator(profile, seed: 42, custodianCountOverride: 2);
        var custodianValues = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 1; i <= 200; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"NATIVES/001/DOC{i:D8}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);
            custodianValues.Add(row["CUSTODIAN"]);
        }

        Assert.True(
            custodianValues.Count <= 2,
            $"Expected at most 2 custodians (Alice, Bob) but got {custodianValues.Count}: {string.Join(", ", custodianValues)}");
        foreach (var v in custodianValues)
        {
            Assert.True(string.Equals(v, "Alice", StringComparison.Ordinal) || string.Equals(v, "Bob", StringComparison.Ordinal), $"Unexpected custodian value after weight truncation: {v}");
        }
    }

    /// <summary>
    /// Test that when custodianCountOverride is applied to a weighted, generated-names source,
    /// the weights are correctly truncated and the output is distributed proportionally among remaining options.
    /// </summary>
    [Fact]
    public void GenerateRow_WithCustodianCountOverride_WeightedGeneratedSource_ShouldDistributeProportionally()
    {
        // Arrange
        var profile = new ColumnProfile
        {
            Name = "generated-weighted-custodians",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>
(StringComparer.Ordinal)
            {
                ["custodians"] = new DataSourceConfig
                {
                    Count = 5,
                    Prefix = "Cust_",
                    Weights = new List<int> { 1, 1, 100, 100, 100 }, // 5 weights
                    Distribution = "weighted",
                },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "CUSTODIAN", Type = "coded", DataSource = "custodians", EmptyPercentage = 0 },
            },
        };

        // Override count to 2. This leaves us with Cust_1 (weight 1) and Cust_2 (weight 1).
        // The remaining weights (100, 100, 100) must be ignored / truncated.
        var generator = new DataGenerator(profile, seed: 12345, custodianCountOverride: 2);
        int sampleSize = 1000;
        int cust1Count = 0;
        int cust2Count = 0;

        for (int i = 1; i <= sampleSize; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            var value = row["CUSTODIAN"];
            if (string.Equals(value, "Cust_1", StringComparison.Ordinal))
            {
                cust1Count++;
            }
            else if (string.Equals(value, "Cust_2", StringComparison.Ordinal))
            {
                cust2Count++;
            }
        }

        // Output should be ~50% Cust_1 and ~50% Cust_2.
        double cust1Percent = (double)cust1Count / sampleSize * 100;
        double cust2Percent = (double)cust2Count / sampleSize * 100;
        Assert.InRange(cust1Percent, 45.0, 55.0);
        Assert.InRange(cust2Percent, 45.0, 55.0);
    }

    /// <summary>
    /// Test that the date format override is applied to date columns without an explicit format.
    /// </summary>
    [Fact]
    public void Constructor_DateFormatOverride_AppliesToDateColumns()
    {
        var profile = new ColumnProfile
        {
            Name = "date-format-override",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DATECREATED", Type = "date", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42, dateFormatOverride: "dd/MM/yyyy");
        var workItem = new FileWorkItem { Index = 1, FilePathInZip = "Folder001/file001.pdf" };
        var fileData = new FileData { Data = new byte[128], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        Assert.Matches(@"^\d{2}/\d{2}/\d{4}$", row["DATECREATED"]);
    }

    /// <summary>
    /// Test that an empty percentage override of zero suppresses empty values and bumps
    /// zero-minimum multi-value counts so every value is populated.
    /// </summary>
    [Fact]
    public void Constructor_EmptyPercentageOverrideZero_ProducesNoEmptyValues()
    {
        var profile = new ColumnProfile
        {
            Name = "empty-override-zero",
            Settings = new ProfileSettings { EmptyValuePercentage = 90 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["tags"] = new DataSourceConfig { Count = 3, Prefix = "Tag_" },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "NOTE", Type = "text", EmptyPercentage = 90 },
                new()
                {
                    Name = "TAGS",
                    Type = "coded",
                    DataSource = "tags",
                    MultiValue = true,
                    MultiValueCount = new RangeConfig { Min = 0, Max = 2 },
                },
            },
        };

        var generator = new DataGenerator(profile, seed: 42, emptyPercentageOverride: 0);

        for (int i = 1; i <= 50; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            Assert.NotEmpty(row["NOTE"]);
            Assert.NotEmpty(row["TAGS"]);
        }
    }

    /// <summary>
    /// Test that an empty percentage override of 100 forces all non-required columns empty
    /// while required columns keep their values.
    /// </summary>
    [Fact]
    public void Constructor_EmptyPercentageOverrideFull_EmptiesNonRequiredColumnsOnly()
    {
        var profile = new ColumnProfile
        {
            Name = "empty-override-full",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "NOTE", Type = "text", EmptyPercentage = 0 },
            },
        };

        var generator = new DataGenerator(profile, seed: 42, emptyPercentageOverride: 100);

        for (int i = 1; i <= 20; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            Assert.NotEmpty(row["DOCID"]);
            Assert.Equal(string.Empty, row["NOTE"]);
        }
    }

    /// <summary>
    /// Test that legacy and synthetic-email column types resolve to their dedicated generators.
    /// </summary>
    [Fact]
    public void GenerateRow_LegacyAndSyntheticEmailColumnTypes_ProduceExpectedShapes()
    {
        var profile = new ColumnProfile
        {
            Name = "legacy-types",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "CUSTODIAN", Type = "indexcustodian", Required = true },
                new() { Name = "DATECREATED", Type = "legacydatecreated", Required = true },
                new() { Name = "FILESIZE", Type = "randomfilesize", Required = true },
                new() { Name = "TO", Type = "syntheticemailto", Required = true },
                new() { Name = "FROM", Type = "syntheticemailfrom", Required = true },
                new() { Name = "SUBJECT", Type = "syntheticemailsubject", Required = true },
                new() { Name = "SENTDATE", Type = "syntheticemailsentdate", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42);
        var workItem = new FileWorkItem { Index = 7, FolderNumber = 3, FilePathInZip = "Folder003/doc7.pdf" };
        var fileData = new FileData { Data = new byte[256], WorkItem = workItem };

        var row = generator.GenerateRow(workItem, fileData);

        Assert.Equal("Custodian 8", row["CUSTODIAN"]); // (7 % 10) + 1
        Assert.NotEmpty(row["DATECREATED"]);
        Assert.True(long.Parse(row["FILESIZE"], System.Globalization.CultureInfo.InvariantCulture) > 0);
        Assert.Contains("@", row["TO"], StringComparison.Ordinal);
        Assert.Contains("@", row["FROM"], StringComparison.Ordinal);
        Assert.NotEmpty(row["SUBJECT"]);
        Assert.NotEmpty(row["SENTDATE"]);
    }

    /// <summary>
    /// Test that a weighted data source whose weights sum to zero falls back to a uniform draw
    /// instead of failing.
    /// </summary>
    [Fact]
    public void GenerateRow_WeightedSourceWithZeroTotalWeight_FallsBackToUniformDraw()
    {
        var profile = new ColumnProfile
        {
            Name = "zero-weights",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["tags"] = new DataSourceConfig
                {
                    Values = new List<string> { "alpha", "beta", "gamma" },
                    Weights = new List<int> { 0, 0, 0 },
                    Distribution = "weighted",
                },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "TAG", Type = "coded", DataSource = "tags", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42);
        var validValues = new HashSet<string>(StringComparer.Ordinal) { "alpha", "beta", "gamma" };

        for (int i = 1; i <= 20; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            Assert.Contains(row["TAG"], validValues);
        }
    }

    /// <summary>
    /// Test that a data source with Gaussian distribution exhibits central concentration and bell-curve characteristics.
    /// </summary>
    [Fact]
    public void GenerateRow_GaussianDataSource_ShowsCentralConcentration()
    {
        var profile = new ColumnProfile
        {
            Name = "gaussian-probe",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["values"] = new DataSourceConfig { Count = 100, Prefix = "V", Distribution = "gaussian" },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "VALUE", Type = "text", DataSource = "values", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42);
        int sampleSize = 1000;
        var indices = new List<int>();
        var counts = new int[100];

        for (int i = 1; i <= sampleSize; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            var val = row["VALUE"];
            Assert.StartsWith("V", val, StringComparison.Ordinal);
            int num = int.Parse(val.Substring(1), System.Globalization.CultureInfo.InvariantCulture); // 1-based: 1..100
            int idx = num - 1; // 0..99
            Assert.InRange(idx, 0, 99);
            indices.Add(idx);
            counts[idx]++;
        }

        // 1. Mean must be centered near 49.5 (middle of 0..99)
        double mean = indices.Average();
        Assert.InRange(mean, 44.0, 55.0);

        // 2. Middle 40% (indices 30..69) must contain majority of values (> 60%),
        //    while outer 40% (0..19 and 80..99) must contain very few values (< 20% combined).
        int middleCount = counts.Skip(30).Take(40).Sum();
        int outerCount = counts.Take(20).Sum() + counts.Skip(80).Take(20).Sum();

        Assert.True(
            middleCount > 600,
            $"Middle 40% should have > 600 of 1000 items in Gaussian distribution, got {middleCount}");
        Assert.True(
            outerCount < 200,
            $"Outer 40% (edges) should have < 200 of 1000 items in Gaussian distribution, got {outerCount}");
        Assert.True(
            middleCount > outerCount * 3,
            $"Middle count ({middleCount}) should be at least 3x the edge count ({outerCount})");
    }

    /// <summary>
    /// Test that a data source with Exponential distribution exhibits low-end bias and monotonic decline across quartiles.
    /// </summary>
    [Fact]
    public void GenerateRow_ExponentialDataSource_ShowsLowEndBias()
    {
        var profile = new ColumnProfile
        {
            Name = "exponential-probe",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["values"] = new DataSourceConfig { Count = 100, Prefix = "V", Distribution = "exponential" },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "VALUE", Type = "text", DataSource = "values", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42);
        int sampleSize = 1000;
        var counts = new int[100];

        for (int i = 1; i <= sampleSize; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            var val = row["VALUE"];
            Assert.StartsWith("V", val, StringComparison.Ordinal);
            int num = int.Parse(val.Substring(1), System.Globalization.CultureInfo.InvariantCulture); // 1-based: 1..100
            int idx = num - 1;
            Assert.InRange(idx, 0, 99);
            counts[idx]++;
        }

        // Quartiles (each 25 items: Q1=0..24, Q2=25..49, Q3=50..74, Q4=75..99)
        int q1 = counts.Take(25).Sum();
        int q2 = counts.Skip(25).Take(25).Sum();
        int q3 = counts.Skip(50).Take(25).Sum();
        int q4 = counts.Skip(75).Take(25).Sum();

        // Monotonic decline across quartiles
        Assert.True(q1 > q2, $"Q1 ({q1}) should be strictly greater than Q2 ({q2}) in exponential distribution");
        Assert.True(q2 > q3, $"Q2 ({q2}) should be strictly greater than Q3 ({q3}) in exponential distribution");
        Assert.True(q3 > q4, $"Q3 ({q3}) should be strictly greater than Q4 ({q4}) in exponential distribution");

        // Q1 should hold ~45% of items, Q4 should hold ~10% of items
        Assert.InRange(q1, 380, 520);
        Assert.InRange(q4, 50, 160);

        // First item (V1) should have more samples than the last item (V100) and no tail spike
        Assert.True(counts[0] > counts[99], $"V1 count ({counts[0]}) should be greater than V100 count ({counts[99]})");
    }

    /// <summary>
    /// Regression test for Issue #829: Uniform, Gaussian, and Exponential distributions
    /// with the same seed must produce distinct outputs and not byte-identical content.
    /// </summary>
    [Fact]
    public void GenerateRow_UniformGaussianExponential_ProduceDistinctOutputs()
    {
        var makeProfile = (string dist) => new ColumnProfile
        {
            Name = $"probe-{dist}",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["values"] = new DataSourceConfig { Count = 100, Prefix = "V", Distribution = dist },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "VALUE", Type = "text", DataSource = "values", Required = true },
            },
        };

        var genUniform = new DataGenerator(makeProfile("uniform"), seed: 42);
        var genGaussian = new DataGenerator(makeProfile("gaussian"), seed: 42);
        var genExponential = new DataGenerator(makeProfile("exponential"), seed: 42);

        var uniformValues = new List<string>();
        var gaussianValues = new List<string>();
        var exponentialValues = new List<string>();

        for (int i = 1; i <= 1000; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };

            uniformValues.Add(genUniform.GenerateRow(workItem, fileData)["VALUE"]);
            gaussianValues.Add(genGaussian.GenerateRow(workItem, fileData)["VALUE"]);
            exponentialValues.Add(genExponential.GenerateRow(workItem, fileData)["VALUE"]);
        }

        // None of the output streams should be identical
        Assert.False(
            uniformValues.SequenceEqual(gaussianValues),
            "Uniform and Gaussian outputs must not be identical");
        Assert.False(
            uniformValues.SequenceEqual(exponentialValues),
            "Uniform and Exponential outputs must not be identical");
        Assert.False(
            gaussianValues.SequenceEqual(exponentialValues),
            "Gaussian and Exponential outputs must not be identical");
    }

    /// <summary>
    /// Test singleton and small pool sizes for Gaussian and Exponential distributions.
    /// </summary>
    [Theory]
    [InlineData("gaussian", 1)]
    [InlineData("exponential", 1)]
    [InlineData("gaussian", 2)]
    [InlineData("exponential", 2)]
    [InlineData("gaussian", 3)]
    [InlineData("exponential", 3)]
    public void GenerateRow_SmallPoolSizes_HandledGracefully(string distribution, int poolSize)
    {
        var profile = new ColumnProfile
        {
            Name = $"small-pool-{distribution}-{poolSize}",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["items"] = new DataSourceConfig { Count = poolSize, Prefix = "Item_", Distribution = distribution },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "ITEM", Type = "text", DataSource = "items", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42);
        var allowed = Enumerable.Range(1, poolSize).Select(i => $"Item_{i}").ToHashSet(StringComparer.Ordinal);

        for (int i = 1; i <= 100; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            Assert.Contains(row["ITEM"], allowed);
        }
    }

    /// <summary>
    /// Test coded column with explicit values and Gaussian/Exponential distributions.
    /// </summary>
    [Theory]
    [InlineData("gaussian")]
    [InlineData("exponential")]
    [InlineData("normal")]
    public void GenerateRow_CodedColumnWithExplicitValues_HonorsDistribution(string distribution)
    {
        var values = new List<string> { "Critical", "High", "Medium", "Low", "Info" };
        var profile = new ColumnProfile
        {
            Name = $"coded-{distribution}",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["severities"] = new DataSourceConfig { Values = values, Distribution = distribution },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "SEVERITY", Type = "coded", DataSource = "severities", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 1; i <= 200; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            Assert.Contains(row["SEVERITY"], values);
            seen.Add(row["SEVERITY"]);
        }

        Assert.True(seen.Count >= 2, "Expected multiple distinct values to be selected");
    }

    /// <summary>
    /// Test that Gaussian and Exponential data sources produce reproducible results with the same seed.
    /// </summary>
    [Theory]
    [InlineData("gaussian")]
    [InlineData("exponential")]
    public void GenerateRow_SeededReproducibility_GaussianAndExponential_ProducesIdenticalResults(string distribution)
    {
        var profile = new ColumnProfile
        {
            Name = $"seed-test-{distribution}",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["source"] = new DataSourceConfig { Count = 50, Prefix = "V", Distribution = distribution },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "VAL", Type = "text", DataSource = "source", Required = true },
            },
        };

        var generator1 = new DataGenerator(profile, seed: 999);
        var generator2 = new DataGenerator(profile, seed: 999);

        for (int i = 1; i <= 100; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };

            var row1 = generator1.GenerateRow(workItem, fileData);
            var row2 = generator2.GenerateRow(workItem, fileData);

            Assert.Equal(row1["VAL"], row2["VAL"]);
        }
    }

    /// <summary>
    /// Test that custodianCountOverride works on Gaussian and Exponential custodian data sources.
    /// </summary>
    [Theory]
    [InlineData("gaussian")]
    [InlineData("exponential")]
    public void GenerateRow_WithCustodianCountOverride_GaussianAndExponential_LimitsDistinctValues(string distribution)
    {
        var profile = new ColumnProfile
        {
            Name = $"cust-override-{distribution}",
            Settings = new ProfileSettings { EmptyValuePercentage = 0 },
            DataSources = new Dictionary<string, DataSourceConfig>(StringComparer.Ordinal)
            {
                ["custodians"] = new DataSourceConfig { Count = 50, Prefix = "Cust_", Distribution = distribution },
            },
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "DOCID", Type = "identifier", Required = true },
                new() { Name = "CUSTODIAN", Type = "text", DataSource = "custodians", Required = true },
            },
        };

        var generator = new DataGenerator(profile, seed: 42, custodianCountOverride: 3);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Cust_1", "Cust_2", "Cust_3" };

        for (int i = 1; i <= 200; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder/doc{i}.pdf" };
            var fileData = new FileData { Data = new byte[128], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            Assert.Contains(row["CUSTODIAN"], allowed);
        }
    }
}

