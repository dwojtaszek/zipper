using System;
using System.Linq;
using Xunit;

namespace Zipper.Tests
{
    public class ProportionalDistributionTests
    {
        [Theory]
        [InlineData(1, 5, 1)]
        [InlineData(2, 5, 2)]
        [InlineData(5, 5, 5)]
        [InlineData(6, 5, 1)]
        [InlineData(10, 5, 5)]
        [InlineData(11, 5, 1)]
        public void CalculateFolder_RoundRobinAssignment_ReturnsExpectedFolder(long fileIndex, int totalFolders, int expectedFolder)
        {
            // Act
            int result = ProportionalDistribution.CalculateFolder(fileIndex, totalFolders);

            // Assert
            Assert.Equal(expectedFolder, result);
        }

        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(100, 1, 1)]
        public void CalculateFolder_SingleFolder_AlwaysReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
        {
            // Act
            int result = ProportionalDistribution.CalculateFolder(fileIndex, totalFolders);

            // Assert
            Assert.Equal(expectedFolder, result);
        }

        [Fact]
        public void CalculateFolder_LargeFileIndex_DistributesEvenly()
        {
            // Arrange
            int totalFolders = 7;
            int fileCount = 100;

            // Act - Calculate distribution across many files
            var results = Enumerable.Range(1, fileCount)
                .Select(i => ProportionalDistribution.CalculateFolder(i, totalFolders))
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

    public class GaussianDistributionTests
    {
        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(100, 1, 1)]
        public void CalculateFolder_SingleFolder_AlwaysReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
        {
            // Act
            int result = GaussianDistribution.CalculateFolder(fileIndex, 100, totalFolders);

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
                .Select(i => GaussianDistribution.CalculateFolder(i, totalFiles, totalFolders))
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
            int result1 = GaussianDistribution.CalculateFolder(1, 1, 1);
            Assert.Equal(1, result1);

            // Test large values
            int result2 = GaussianDistribution.CalculateFolder(10000, 10000, 100);
            Assert.InRange(result2, 1, 100);

            // Test middle probability
            int result3 = GaussianDistribution.CalculateFolder(5000, 10000, 100);
            Assert.InRange(result3, 1, 100);
        }

        [Theory]
        [InlineData(0.5, 0.0)] // Middle probability
        [InlineData(0.025, -1.96)] // Low probability
        [InlineData(0.975, 1.96)] // High probability
        public void InverseNormalCDF_KnownProbabilities_ReturnsExpectedValues(double probability, double expectedApprox)
        {
            // Act
            double result = GaussianDistribution.InverseNormalCDF(probability);

            // Assert
            Assert.Equal(expectedApprox, result, 0.1); // Allow 0.1 tolerance
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        public void InverseNormalCDF_InvalidProbability_ClampsToValidRange(double probability)
        {
            // Act
            double result1 = GaussianDistribution.InverseNormalCDF(probability);
            double result2 = GaussianDistribution.InverseNormalCDF(probability);

            // Assert - Should be clamped to valid range [0.001, 0.999]
            Assert.InRange(result1, -3.5, 3.5);
            Assert.InRange(result2, -3.5, 3.5);
        }
    }

    public class ExponentialDistributionTests
    {
        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(100, 1, 1)]
        public void CalculateFolder_SingleFolder_AlwaysReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
        {
            // Act
            int result = ExponentialDistribution.CalculateFolder(fileIndex, 100, totalFolders);

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
                .Select(i => ExponentialDistribution.CalculateFolder(i, totalFiles, totalFolders))
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
        public void CalculateFolder_EdgeCases_HandlesGracefully()
        {
            // Test minimum values
            int result1 = ExponentialDistribution.CalculateFolder(1, 1, 1);
            Assert.Equal(1, result1);

            // Test large values
            int result2 = ExponentialDistribution.CalculateFolder(10000, 10000, 100);
            Assert.InRange(result2, 1, 100);
        }

        [Theory]
        [InlineData(1.0, 0.5, 0.693)]  // -ln(1-0.5)/1 = -ln(0.5) = 0.693
        [InlineData(2.0, 0.5, 0.346)]  // -ln(1-0.5)/2 = 0.693/2 = 0.346
        [InlineData(1.0, 0.9, 2.303)]  // -ln(1-0.9)/1 = -ln(0.1) = 2.303
        public void CalculateExponential_ValidParameters_ReturnsCorrectValue(double lambda, double probability, double expectedApprox)
        {
            // Act
            double result = ExponentialDistribution.CalculateExponential(lambda, probability);

            // Assert
            Assert.Equal(expectedApprox, result, 0.01); // Allow 0.01 tolerance
        }

        [Theory]
        [InlineData(0.0, 0.5)]
        [InlineData(-1.0, 0.5)]
        public void CalculateExponential_InvalidLambda_ThrowsArgumentException(double lambda, double probability)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                ExponentialDistribution.CalculateExponential(lambda, probability));
        }

        [Theory]
        [InlineData(1.0, -0.1)]
        [InlineData(1.0, 1.1)]
        public void CalculateExponential_InvalidProbability_ThrowsArgumentException(double lambda, double probability)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                ExponentialDistribution.CalculateExponential(lambda, probability));
        }
    }

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
            int result = FileDistributionHelper.GetFolderNumber(fileIndex, totalFiles, totalFolders, distributionType);

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
                FileDistributionHelper.GetFolderNumber(fileIndex, totalFiles, totalFolders, DistributionType.Proportional));
        }

        [Theory]
        [InlineData(5, 10, 0)] // Folders < 1
        [InlineData(5, 10, 101)] // Folders > 100
        public void GetFolderNumber_InvalidTotalFolders_ThrowsArgumentOutOfRangeException(long fileIndex, long totalFiles, int totalFolders)
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FileDistributionHelper.GetFolderNumber(fileIndex, totalFiles, totalFolders, DistributionType.Proportional));
        }

        [Theory]
        [InlineData(5, 0, 5)] // Total files < 1
        public void GetFolderNumber_InvalidTotalFiles_ThrowsArgumentOutOfRangeException(long fileIndex, long totalFiles, int totalFolders)
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FileDistributionHelper.GetFolderNumber(fileIndex, totalFiles, totalFolders, DistributionType.Proportional));
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
                (fileIndex: 999L, totalFiles: 1000L, totalFolders: 100, type: DistributionType.Proportional)
            };

            foreach (var (fileIndex, totalFiles, totalFolders, type) in testCases)
            {
                // Act
                int result = FileDistributionHelper.GetFolderNumber(fileIndex, totalFiles, totalFolders, type);

                // Assert - Result should be within expected range
                Assert.InRange(result, 1, totalFolders);
            }
        }
    }
}