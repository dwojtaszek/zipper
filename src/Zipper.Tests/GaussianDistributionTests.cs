using Xunit;

namespace Zipper.Tests;

public class GaussianDistributionTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(100, 1, 1)]
    public void Gaussian_SingleFolder_ReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
    {
        // Act
        int result = Distributions.Gaussian(fileIndex, 100, totalFolders);

        // Assert
        Assert.Equal(expectedFolder, result);
    }

    [Fact]
    public void Gaussian_MultipleFiles_ReturnsValidFolderRange()
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
            .ToDictionary(g => g.Key, g => g.Count());

        // Stricter center-vs-edge assertions to distinguish Gaussian from uniform distribution
        Assert.True(distribution.ContainsKey(5) && distribution[5] > 200, "Center folder 5 should have > 200 files in Gaussian distribution.");
        Assert.True(distribution.ContainsKey(1) && distribution[1] < 30, "Edge folder 1 should have < 30 files in Gaussian distribution.");
        Assert.True(distribution.ContainsKey(10) && distribution[10] < 10, "Edge folder 10 should have < 10 files in Gaussian distribution.");
        Assert.True(distribution[5] > distribution[1] * 5, "Center folder 5 should have at least 5x more files than edge folder 1.");
    }

    [Fact]
    public void Gaussian_EdgeCases_ReturnsFolderInRange()
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
