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
    [InlineData(10, 100, 51, 3)]
    [InlineData(10, 100, 100, 10)]
    [InlineData(100, 100, 1, 1)]
    [InlineData(100, 100, 50, 28)]
    [InlineData(100, 100, 100, 100)]
    public void Exponential_WithLambda2_ReturnsExpectedFolders(int totalFolders, long totalFiles, long fileIndex, int expectedFolder)
    {
        // With normalized exponential distribution (lambda = 2.0/totalFolders),
        // verify known folder assignments without tail clamping distortion.
        int result = Distributions.Exponential(fileIndex, totalFiles, totalFolders);
        Assert.Equal(expectedFolder, result);
    }

    [Fact]
    public void Exponential_Lambda2_SpreadWithinExpectedRange()
    {
        // With lambda = 2.0/totalFolders, verify no single folder
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

    [Fact]
    public void Exponential_Histogram_100kFiles100Folders_NoTailSpikeAndStrictlyNonIncreasing()
    {
        // Verified output-shape defect from Issue #828:
        // 100k files and 100 folders previously caused folder 100 to contain ~13,807 files (a 7x spike over folder 1).
        // Normalized exponential distribution must ensure:
        // 1. Folder 1 contains the most files (~2,291).
        // 2. Folder 100 contains the least files (~317).
        // 3. Occupancy across all folders 1..100 is non-increasing.
        // 4. Endpoints correctly map file 1 -> folder 1 and file 100,000 -> folder 100.
        const int totalFiles = 100_000;
        const int totalFolders = 100;

        var counts = new int[totalFolders + 1];
        for (int i = 1; i <= totalFiles; i++)
        {
            int folder = Distributions.Exponential(i, totalFiles, totalFolders);
            counts[folder]++;
        }

        // Total count must be exact
        Assert.Equal(totalFiles, counts.Skip(1).Sum());

        // Valid endpoints
        Assert.Equal(1, Distributions.Exponential(1, totalFiles, totalFolders));
        Assert.Equal(totalFolders, Distributions.Exponential(totalFiles, totalFiles, totalFolders));

        // Folder 1 must have more files than Folder 100
        Assert.True(counts[1] > counts[totalFolders], $"Folder 1 ({counts[1]}) should have more files than Folder 100 ({counts[totalFolders]})");

        // Folder 100 must NOT spike above Folder 99
        Assert.True(counts[totalFolders] <= counts[totalFolders - 1], $"Folder 100 ({counts[totalFolders]}) spiked above Folder 99 ({counts[totalFolders - 1]})");

        // Monotonically non-increasing occupancy across all folders
        for (int k = 1; k < totalFolders; k++)
        {
            Assert.True(
                counts[k] >= counts[k + 1],
                $"Folder {k} ({counts[k]}) had fewer files than Folder {k + 1} ({counts[k + 1]})");
        }
    }

    [Theory]
    [InlineData(1000, 10)]
    [InlineData(1000, 20)]
    [InlineData(5000, 50)]
    [InlineData(20, 5)]
    [InlineData(100000, 100)]
    public void Exponential_Histogram_RepresentativeConfigurations_NonIncreasingOccupancy(int totalFiles, int totalFolders)
    {
        var counts = new int[totalFolders + 1];
        for (int i = 1; i <= totalFiles; i++)
        {
            int folder = Distributions.Exponential(i, totalFiles, totalFolders);
            counts[folder]++;
        }

        // Total count must be exact
        Assert.Equal(totalFiles, counts.Skip(1).Sum());

        // Endpoints
        Assert.Equal(1, Distributions.Exponential(1, totalFiles, totalFolders));
        Assert.Equal(totalFolders, Distributions.Exponential(totalFiles, totalFiles, totalFolders));

        // No artificial final-folder spike
        Assert.True(counts[totalFolders] <= counts[totalFolders - 1]);

        // Non-increasing occupancy across folders
        for (int k = 1; k < totalFolders; k++)
        {
            Assert.True(
                counts[k] >= counts[k + 1],
                $"Configuration ({totalFiles}, {totalFolders}): Folder {k} ({counts[k]}) < Folder {k + 1} ({counts[k + 1]})");
        }
    }

    [Fact]
    public void Exponential_MoreFoldersThanFiles_DistributesWithinValidRange()
    {
        // When totalFolders > totalFiles, each file is assigned to a valid folder in non-decreasing order
        const int totalFiles = 5;
        const int totalFolders = 20;

        var assignedFolders = new List<int>();
        for (int i = 1; i <= totalFiles; i++)
        {
            int folder = Distributions.Exponential(i, totalFiles, totalFolders);
            assignedFolders.Add(folder);
        }

        // Must be in valid range [1, 20]
        Assert.All(assignedFolders, f => Assert.InRange(f, 1, totalFolders));

        // First file is folder 1, last file is folder 20
        Assert.Equal(1, assignedFolders[0]);
        Assert.Equal(20, assignedFolders[^1]);

        // Sequence of folder assignments should be monotonically non-decreasing
        for (int i = 0; i < assignedFolders.Count - 1; i++)
        {
            Assert.True(assignedFolders[i] <= assignedFolders[i + 1]);
        }
    }

    [Fact]
    public void Exponential_ExtremeCounts_HandledCorrectly()
    {
        // Single file, single folder
        Assert.Equal(1, Distributions.Exponential(1, 1, 1));

        // Single file, max folders (100)
        Assert.Equal(1, Distributions.Exponential(1, 1, 100));

        // Many files, single folder
        for (int i = 1; i <= 50; i++)
        {
            Assert.Equal(1, Distributions.Exponential(i, 50, 1));
        }

        // Large file count (10 million files)
        const long largeTotalFiles = 10_000_000L;
        const int folders = 100;
        Assert.Equal(1, Distributions.Exponential(1, largeTotalFiles, folders));
        Assert.Equal(100, Distributions.Exponential(largeTotalFiles, largeTotalFiles, folders));
        int midFolder = Distributions.Exponential(largeTotalFiles / 2, largeTotalFiles, folders);
        Assert.InRange(midFolder, 1, folders);
    }
}
