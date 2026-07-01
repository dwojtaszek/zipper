using Xunit;

namespace Zipper.Tests;

public class ProportionalDistributionTests
{
    [Theory]
    [InlineData(1, 5, 1)]
    [InlineData(2, 5, 2)]
    [InlineData(5, 5, 5)]
    [InlineData(6, 5, 1)]
    [InlineData(10, 5, 5)]
    [InlineData(11, 5, 1)]
    public void Proportional_RoundRobinAssignment_ReturnsExpectedFolder(long fileIndex, int totalFolders, int expectedFolder)
    {
        // Act
        int result = Distributions.Proportional(fileIndex, totalFolders);

        // Assert
        Assert.Equal(expectedFolder, result);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(100, 1, 1)]
    public void Proportional_SingleFolder_ReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
    {
        // Act
        int result = Distributions.Proportional(fileIndex, totalFolders);

        // Assert
        Assert.Equal(expectedFolder, result);
    }

    [Fact]
    public void Proportional_LargeFileIndex_DistributesEvenly()
    {
        // Arrange
        int totalFolders = 7;
        int fileCount = 100;

        // Act - Calculate distribution across many files
        var results = Enumerable.Range(1, fileCount)
            .Select(i => Distributions.Proportional(i, totalFolders))
            .GroupBy(folder => folder)
            .ToDictionary(g => g.Key, g => g.Count());

        // Assert - Each folder should have roughly equal distribution
        int expectedPerFolder = fileCount / totalFolders;
        int expectedRemainder = fileCount % totalFolders;

        for (int folder = 1; folder <= totalFolders; folder++)
        {
            int expectedCount = folder <= expectedRemainder ? expectedPerFolder + 1 : expectedPerFolder;
            Assert.True(results.ContainsKey(folder));
            Assert.Equal(expectedCount, results[folder]);
        }
    }
}
