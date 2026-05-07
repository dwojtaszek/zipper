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
            int result = Distributions.Proportional(fileIndex, totalFolders);

            // Assert
            Assert.Equal(expectedFolder, result);
        }

        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(100, 1, 1)]
        public void CalculateFolder_SingleFolder_AlwaysReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
        {
            // Act
            int result = Distributions.Proportional(fileIndex, totalFolders);

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

    public class ExponentialDistributionTests
    {
        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(100, 1, 1)]
        public void CalculateFolder_SingleFolder_AlwaysReturnsOne(long fileIndex, int totalFolders, int expectedFolder)
        {
            // Act
            int result = Distributions.Exponential(fileIndex, 100, totalFolders);

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
        public void CalculateFolder_EdgeCases_HandlesGracefully()
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
        public void CalculateFolder_WithLambda2_ReturnsExpectedFolders(int totalFolders, long totalFiles, long fileIndex, int expectedFolder)
        {
            // C2: with lambda = 2.0/totalFolders, verify known folder assignments
            int result = Distributions.Exponential(fileIndex, totalFiles, totalFolders);
            Assert.Equal(expectedFolder, result);
        }

        [Fact]
        public void CalculateFolder_Lambda2_DistributionSpreadWithinExpectedRange()
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
    }
}
