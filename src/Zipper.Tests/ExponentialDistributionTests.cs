using Xunit;

namespace Zipper.Tests;

public class ExponentialDistributionTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(100, 1, 1)]
    public void Exponential_SingleFolder_ReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
    {
        // Act
        int result = Distributions.Exponential(fileIndex, 100, totalFolders);

        // Assert
        Assert.Equal(expectedFolder, result);
    }

    [Fact]
    public void Exponential_MultipleFiles_ReturnsValidFolderRange()
    {
        // Arrange
        int totalFiles = 1000;
        int totalFolders = 10;

        // Act - Generate folder numbers for all files
        var folderNumbers = Enumerable.Range(1, totalFiles)
            .Select(i => Distributions.Exponential(i, totalFiles, totalFolders))
            .ToList();

        // Assert - All results should be within valid range
        Assert.All(folderNumbers, folder => Assert.InRange(folder, 1, totalFolders));

        // Verify exponential distribution characteristics (more files in early folders)
        var distribution = folderNumbers.GroupBy(f => f)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderBy(kvp => kvp.Key);

        // Early folders should have more files than later folders (exponential characteristic)
        int earlyFoldersSum = distribution.Take(totalFolders / 2).Sum(kvp => kvp.Value);
        int laterFoldersSum = distribution.Skip(totalFolders / 2).Sum(kvp => kvp.Value);
        Assert.True(earlyFoldersSum > laterFoldersSum);
    }

    [Fact]
    public void Exponential_EdgeCases_ReturnsFolderInRange()
    {
        // Test minimum values
        int result1 = Distributions.Exponential(1, 1, 1);
        Assert.Equal(1, result1);

        // Test large values
        int result2 = Distributions.Exponential(10000, 10000, 100);
        Assert.InRange(result2, 1, 100);
    }

    [Theory]
    [InlineData(10, 100, 1, 1)]
    [InlineData(10, 100, 51, 4)]
    [InlineData(10, 100, 100, 10)]
    [InlineData(100, 100, 1, 1)]
    public void Exponential_WithLambda2_ReturnsExpectedFolders(int totalFolders, long totalFiles, long fileIndex, int expectedFolder)
    {
        // C2: with lambda = 2.0/totalFolders, verify known folder assignments
        int result = Distributions.Exponential(fileIndex, totalFiles, totalFolders);
        Assert.Equal(expectedFolder, result);
    }

    [Fact]
    public void Exponential_Lambda2_SpreadWithinExpectedRange()
    {
        // C2: with lambda = 2.0/totalFolders, verify no single folder
        // gets more than 35% of files (would indicate concentration bug)
        int totalFiles = 1000;
        int totalFolders = 20;

        var folderNumbers = Enumerable.Range(1, totalFiles)
            .Select(i => Distributions.Exponential(i, totalFiles, totalFolders))
            .GroupBy(f => f)
            .ToDictionary(g => g.Key, g => g.Count());

        int maxCount = folderNumbers.Values.Max();
        double maxPercent = (double)maxCount / totalFiles;

        Assert.True(
            maxPercent < 0.35,
            $"Folder {folderNumbers.First(kv => kv.Value == maxCount).Key} has {maxPercent:P1} of files, expected < 35%");
    }
}
