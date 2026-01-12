// <copyright file="ExponentialDistribution.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Zipper
{
    /// <summary>
    /// Exponential distribution calculator with O(1) calculations
    /// Provides mathematical operations for file folder distribution.
    /// </summary>
    public static class ExponentialDistribution
    {
        /// <summary>
        /// Calculates folder number using exponential distribution.
        /// </summary>
        /// <param name="fileIndex">Current file index (1-based).</param>
        /// <param name="totalFiles">Total number of files.</param>
        /// <param name="totalFolders">Total number of folders (1-100).</param>
        /// <returns>Folder number (1 to totalFolders).</returns>
        public static int CalculateFolder(long fileIndex, long totalFiles, int totalFolders)
        {
            // Handle edge case for single folder
            if (totalFolders == 1)
            {
                return 1;
            }

            // Exponential distribution parameter
            double lambda = 3.0 / totalFolders;

            // Convert file index to normalized position (0 to 1)
            double normalizedPosition = (fileIndex - 1.0) / Math.Max(totalFiles - 1.0, 1.0);

            // Use inverse exponential CDF: -ln(1-p)/λ
            // Adjust to prevent ln(0) by using (1 - normalizedPosition) with small epsilon
            double adjustedPosition = Math.Max(0.001, Math.Min(0.999, normalizedPosition));
            double exponentialValue = -Math.Log(1.0 - adjustedPosition) / lambda;

            // Map to folder number (ceiling to ensure at least folder 1)
            int folder = (int)Math.Ceiling(exponentialValue);

            // Clamp to valid range
            return Math.Max(1, Math.Min(totalFolders, folder));
        }

        /// <summary>
        /// Calculates exponential distribution value with given lambda parameter.
        /// </summary>
        /// <param name="lambda">Rate parameter (must be > 0).</param>
        /// <param name="probability">Probability value (0 to 1).</param>
        /// <returns>Exponential distribution value.</returns>
        public static double CalculateExponential(double lambda, double probability)
        {
            if (lambda <= 0)
            {
                throw new ArgumentException("Lambda must be positive", nameof(lambda));
            }

            if (probability < 0 || probability > 1)
            {
                throw new ArgumentException("Probability must be between 0 and 1", nameof(probability));
            }

            // Clamp probability to avoid ln(0)
            double adjustedProbability = Math.Max(0.001, Math.Min(0.999, probability));

            return -Math.Log(1.0 - adjustedProbability) / lambda;
        }
    }
}
