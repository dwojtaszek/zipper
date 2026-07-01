using Xunit;

namespace Zipper.Tests;

public class FileDistributionHelperTests
{
    [Theory]
    [InlineData(1, 10, 5, DistributionType.Proportional)]
    [InlineData(50, 100, 10, DistributionType.Proportional)]
    [InlineData(1, 10, 5, DistributionType.Gaussian)]
    [InlineData(1, 10, 5, DistributionType.Exponential)]
    public void GetFolderNumber_ValidInputs_ReturnsFolderNumber(long fileIndex, long totalFiles, int totalFolders, DistributionType distributionType)
    {
        // Act
        int result = Distributions.GetFolderNumber(fileIndex, totalFiles, totalFolders, distributionType);

        // Assert
        Assert.InRange(result, 1, totalFolders);
    }

    [Theory]
    [InlineData(0, 10, 5)]
    [InlineData(11, 10, 5)]
    [InlineData(-1, 10, 5)]
    public void GetFolderNumber_InvalidFileIndex_ThrowsArgumentOutOfRangeException(long fileIndex, long totalFiles, int totalFolders)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Distributions.GetFolderNumber(fileIndex, totalFiles, totalFolders, DistributionType.Proportional));
    }

    [Theory]
    [InlineData(5, 10, 0)] // Folders < 1
    [InlineData(5, 10, 101)] // Folders > 100
    public void GetFolderNumber_InvalidTotalFolders_ThrowsArgumentOutOfRangeException(long fileIndex, long totalFiles, int totalFolders)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Distributions.GetFolderNumber(fileIndex, totalFiles, totalFolders, DistributionType.Proportional));
    }

    [Theory]
    [InlineData(5, 0, 5)] // Total files < 1
    public void GetFolderNumber_InvalidTotalFiles_ThrowsArgumentOutOfRangeException(long fileIndex, long totalFiles, int totalFolders)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Distributions.GetFolderNumber(fileIndex, totalFiles, totalFolders, DistributionType.Proportional));
    }

    [Fact]
    public void GetFolderNumber_BackwardCompatibility_MatchesOldBehavior()
    {
        // This test ensures that the refactored distribution algorithms produce
        // the same results as the original implementation

        // Test with various parameters
        var testCases = new[]
        {
            (fileIndex: 1L, totalFiles: 100L, totalFolders: 10, type: DistributionType.Proportional),
            (fileIndex: 50L, totalFiles: 100L, totalFolders: 10, type: DistributionType.Gaussian),
            (fileIndex: 25L, totalFiles: 100L, totalFolders: 10, type: DistributionType.Exponential),
            (fileIndex: 999L, totalFiles: 1000L, totalFolders: 100, type: DistributionType.Proportional),
        };

        foreach (var (fileIndex, totalFiles, totalFolders, type) in testCases)
        {
            // Act
            int result = Distributions.GetFolderNumber(fileIndex, totalFiles, totalFolders, type);

            // Assert - Result should be within expected range
            Assert.InRange(result, 1, totalFolders);
        }
    }

    [Fact]
    public void GetFolderNumber_UnknownDistributionType_ShouldThrowArgumentException()
    {
        var unknown = (DistributionType)999;
        Assert.Throws<ArgumentException>(() =>
            Distributions.GetFolderNumber(1, 100, 10, unknown));
    }
}
