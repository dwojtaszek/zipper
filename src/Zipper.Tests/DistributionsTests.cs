using Xunit;

namespace Zipper.Tests;

public class DistributionsTests
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
            (fileIndex: 1L, totalFiles: 100L, totalFolders: 10, type: DistributionType.Proportional, expected: 1),
            (fileIndex: 50L, totalFiles: 100L, totalFolders: 10, type: DistributionType.Gaussian, expected: 5),
            (fileIndex: 25L, totalFiles: 100L, totalFolders: 10, type: DistributionType.Exponential, expected: 2),
            (fileIndex: 999L, totalFiles: 1000L, totalFolders: 100, type: DistributionType.Proportional, expected: 99),
        };

        foreach (var (fileIndex, totalFiles, totalFolders, type, expected) in testCases)
        {
            // Act
            int result = Distributions.GetFolderNumber(fileIndex, totalFiles, totalFolders, type);

            // Assert
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void GetFolderNumber_UnknownDistributionType_ShouldThrowArgumentException()
    {
        var unknown = (DistributionType)999;
        Assert.Throws<ArgumentException>(() =>
            Distributions.GetFolderNumber(1, 100, 10, unknown));
    }

    [Fact]
    public void Proportional_WithBoundaryFileIndices_ReturnsExpectedFolder()
    {
        Assert.Equal(1, Distributions.Proportional(1, 5));
        Assert.Equal(5, Distributions.Proportional(5, 5));
        Assert.Equal(1, Distributions.Proportional(6, 5));
        Assert.Equal(1, Distributions.GetFolderNumber(1, 1, 5, DistributionType.Proportional));
    }

    [Fact]
    public void Gaussian_WithSingleTotalFile_ReturnsFirstFolder()
    {
        Assert.Equal(1, Distributions.Gaussian(1, 1, 10));
        Assert.Equal(1, Distributions.GetFolderNumber(1, 1, 10, DistributionType.Gaussian));
    }

    [Fact]
    public void Gaussian_SampleMean_CentersNearHalfTotalFolders()
    {
        const int totalFolders = 20;
        const int totalFiles = 10000;

        double sum = 0;
        for (int i = 1; i <= totalFiles; i++)
        {
            sum += Distributions.Gaussian(i, totalFiles, totalFolders);
        }

        double mean = sum / totalFiles;
        Assert.InRange(mean, 9.7, 10.3);
    }

    [Fact]
    public void Gaussian_WithSingleFolder_AlwaysReturnsFirstFolder()
    {
        Assert.Equal(1, Distributions.Gaussian(1, 100, 1));
        Assert.Equal(1, Distributions.Gaussian(100, 100, 1));
    }

    [Fact]
    public void Gaussian_FirstAndLastFile_ReturnEndpointFolders()
    {
        const int totalFolders = 20;
        const int totalFiles = 1000;

        Assert.Equal(1, Distributions.Gaussian(1, totalFiles, totalFolders));
        Assert.Equal(totalFolders, Distributions.Gaussian(totalFiles, totalFiles, totalFolders));
    }

    [Fact]
    public void Gaussian_AsFileIndexIncreases_FolderIsMonotonicallyNonDecreasing()
    {
        const int totalFolders = 20;
        const int totalFiles = 1000;

        int previous = Distributions.Gaussian(1, totalFiles, totalFolders);
        for (int i = 2; i <= totalFiles; i++)
        {
            int current = Distributions.Gaussian(i, totalFiles, totalFolders);
            Assert.True(current >= previous, $"Folder regressed at file {i}: {previous} -> {current}");
            previous = current;
        }
    }

    [Fact]
    public void Exponential_FirstQuarterFolders_ReceivesExpectedShareOfFiles()
    {
        Assert.Equal(1, Distributions.Exponential(1, 1, 10));

        const int totalFolders = 20;
        const int totalFiles = 10000;
        int firstQuarterCutoff = totalFolders / 4;
        int firstQuarterCount = 0;

        for (int i = 1; i <= totalFiles; i++)
        {
            if (Distributions.Exponential(i, totalFiles, totalFolders) <= firstQuarterCutoff)
            {
                firstQuarterCount++;
            }
        }

        double share = (double)firstQuarterCount / totalFiles;
        Assert.InRange(share, 0.35, 0.55);
    }
}
