using System;

namespace Zipper
{
    public enum DistributionType
    {
        Proportional,
        Gaussian,
        Exponential
    }

    /// <summary>
    /// File distribution helper that delegates to specialized distribution algorithms
    /// Maintains backward compatibility while using extracted distribution classes
    /// </summary>
    public static class FileDistributionHelper
    {
        /// <summary>
        /// Gets the folder number for a file based on the specified distribution type
        /// </summary>
        /// <param name="fileIndex">Current file index (1-based)</param>
        /// <param name="totalFiles">Total number of files</param>
        /// <param name="totalFolders">Total number of folders (1-100)</param>
        /// <param name="distributionType">Type of distribution to use</param>
        /// <returns>Folder number (1 to totalFolders)</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when input parameters are out of valid ranges</exception>
        /// <exception cref="ArgumentException">Thrown when distribution type is unknown</exception>
        public static int GetFolderNumber(long fileIndex, long totalFiles, int totalFolders, DistributionType distributionType)
        {
            // Input validation
            if (fileIndex < 1 || fileIndex > totalFiles)
                throw new ArgumentOutOfRangeException(nameof(fileIndex), "File index must be between 1 and total files");

            if (totalFolders < 1 || totalFolders > 100)
                throw new ArgumentOutOfRangeException(nameof(totalFolders), "Total folders must be between 1 and 100");

            if (totalFiles < 1)
                throw new ArgumentOutOfRangeException(nameof(totalFiles), "Total files must be at least 1");

            return distributionType switch
            {
                DistributionType.Proportional => ProportionalDistribution.CalculateFolder(fileIndex, totalFolders),
                DistributionType.Gaussian => GaussianDistribution.CalculateFolder(fileIndex, totalFiles, totalFolders),
                DistributionType.Exponential => ExponentialDistribution.CalculateFolder(fileIndex, totalFiles, totalFolders),
                _ => throw new ArgumentException($"Unknown distribution type: {distributionType}", nameof(distributionType))
            };
        }
    }
}
