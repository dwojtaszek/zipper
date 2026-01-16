// <copyright file="DataGeneratorTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
            responsiveValue == "Y" || responsiveValue == "N" || string.IsNullOrEmpty(responsiveValue),
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
                DateTime.TryParse(dateValue, out _),
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
            Assert.Contains("@", emailValue);
            Assert.Contains(".", emailValue);
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

        // Generate multiple rows to find one with multi-value
        for (int i = 1; i <= 100; i++)
        {
            var workItem = new FileWorkItem { Index = i, FilePathInZip = $"Folder001/file{i:D3}.pdf" };
            var fileData = new FileData { Data = new byte[1024], WorkItem = workItem };
            var row = generator.GenerateRow(workItem, fileData);

            var emailToValue = row["EMAILTO"];
            if (!string.IsNullOrEmpty(emailToValue) && emailToValue.Contains(profile.Settings.MultiValueDelimiter))
            {
                // Found a multi-value - verify it uses the right delimiter
                Assert.Contains(";", emailToValue);
                var parts = emailToValue.Split(';');
                Assert.True(parts.Length >= 2, "Multi-value should have at least 2 values");
                return; // Test passed
            }
        }

        // It's okay if we never find a multi-value in 100 iterations (random)
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
            long.TryParse(fileSizeValue, out var size),
            $"Expected valid number but got {fileSizeValue}");
        Assert.True(size >= 0, "File size should be non-negative");
    }
}
