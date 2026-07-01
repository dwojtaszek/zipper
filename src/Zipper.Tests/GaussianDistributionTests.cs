using Xunit;

namespace Zipper.Tests;

public class GaussianDistributionTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(100, 1, 1)]
    public void CalculateFolder_SingleFolder_AlwaysReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
    {
        // Act
        int result = Distributions.Gaussian(fileIndex, 100, totalFolders);

        // Assert
        Assert.Equal(expectedFolder, result);
    }

    [Fact]
    public void CalculateFolder_MultipleFiles_ReturnsValidFolderRange()
    {
        // Arrange
        int totalFiles = 1000;
        int totalFolders = 10;

        // Act - Generate folder numbers for all files
        var folderNumbers = Enumerable.Range(1, totalFiles)
            .Select(i => Distributions.Gaussian(i, totalFiles, totalFolders))
            .ToList();

        // Assert - All results should be within valid range
        Assert.All(folderNumbers, folder => Assert.InRange(folder, 1, totalFolders));

        // Verify Gaussian distribution characteristics (bell curve)
        var distribution = folderNumbers.GroupBy(f => f)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderBy(kvp => kvp.Key);

        // Middle folders should have more files than edge folders (Gaussian characteristic)
        Assert.True(distribution.Skip(totalFolders / 3).Take(totalFolders / 3)
            .Sum(kvp => kvp.Value) > distribution.Take(2).Sum(kvp => kvp.Value));
    }

    [Fact]
    public void CalculateFolder_EdgeCases_HandlesGracefully()
    {
        // Test minimum values
        int result1 = Distributions.Gaussian(1, 1, 1);
        Assert.Equal(1, result1);

        // Test large values
        int result2 = Distributions.Gaussian(10000, 10000, 100);
        Assert.InRange(result2, 1, 100);

        // Test middle probability
        int result3 = Distributions.Gaussian(5000, 10000, 100);
        Assert.InRange(result3, 1, 100);
    }
}
