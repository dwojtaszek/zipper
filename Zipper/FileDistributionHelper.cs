using System;

namespace Zipper
{
    public enum DistributionType
    {
        Proportional,
        Gaussian,
        Exponential
    }

    public static class FileDistributionHelper
    {
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
                DistributionType.Proportional => GetProportionalFolder(fileIndex, totalFolders),
                DistributionType.Gaussian => GetGaussianFolder(fileIndex, totalFiles, totalFolders),
                DistributionType.Exponential => GetExponentialFolder(fileIndex, totalFiles, totalFolders),
                _ => throw new ArgumentException($"Unknown distribution type: {distributionType}", nameof(distributionType))
            };
        }

        private static int GetProportionalFolder(long fileIndex, int totalFolders)
        {
            // Round-robin assignment (existing logic)
            return (int)((fileIndex - 1) % totalFolders) + 1;
        }

        private static int GetGaussianFolder(long fileIndex, long totalFiles, int totalFolders)
        {
            // Handle edge case for single folder
            if (totalFolders == 1) return 1;

            // Parameters for normal distribution
            double mean = totalFolders / 2.0;
            double stdDev = totalFolders / 6.0; // Ensures ~99.7% within range

            // Convert file index to normalized position (0 to 1)
            double normalizedPosition = (fileIndex - 1.0) / Math.Max(totalFiles - 1.0, 1.0);

            // Use Box-Muller transformation to convert uniform to normal distribution
            // For simplicity, use inverse normal CDF approximation
            double z = InverseNormalCDF(normalizedPosition);
            
            // Map to folder number
            double folderDouble = mean + stdDev * z;
            int folder = (int)Math.Round(folderDouble);

            // Clamp to valid range
            return Math.Max(1, Math.Min(totalFolders, folder));
        }

        private static int GetExponentialFolder(long fileIndex, long totalFiles, int totalFolders)
        {
            // Handle edge case for single folder
            if (totalFolders == 1) return 1;

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
        /// Approximation of inverse normal CDF using Beasley-Springer-Moro algorithm
        /// </summary>
        private static double InverseNormalCDF(double p)
        {
            // Clamp input to valid range
            p = Math.Max(0.001, Math.Min(0.999, p));

            // Constants for approximation
            const double a0 = -3.969683028665376e+01;
            const double a1 = 2.209460984245205e+02;
            const double a2 = -2.759285104469687e+02;
            const double a3 = 1.383577518672690e+02;
            const double a4 = -3.066479806614716e+01;
            const double a5 = 2.506628277459239e+00;

            const double b1 = -5.447609879822406e+01;
            const double b2 = 1.615858368580409e+02;
            const double b3 = -1.556989798598866e+02;
            const double b4 = 6.680131188771972e+01;
            const double b5 = -1.328068155288572e+01;

            const double c0 = -7.784894002430293e-03;
            const double c1 = -3.223964580411365e-01;
            const double c2 = -2.400758277161838e+00;
            const double c3 = -2.549732539343734e+00;
            const double c4 = 4.374664141464968e+00;
            const double c5 = 2.938163982698783e+00;

            const double d1 = 7.784695709041462e-03;
            const double d2 = 3.224671290700398e-01;
            const double d3 = 2.445134137142996e+00;
            const double d4 = 3.754408661907416e+00;

            // Define break-points
            const double pLow = 0.02425;
            const double pHigh = 1 - pLow;

            if (p < pLow)
            {
                // Rational approximation for lower region
                double q = Math.Sqrt(-2 * Math.Log(p));
                return (((((c0 * q + c1) * q + c2) * q + c3) * q + c4) * q + c5) /
                       ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
            else if (p <= pHigh)
            {
                // Rational approximation for central region
                double q = p - 0.5;
                double r = q * q;
                return (((((a0 * r + a1) * r + a2) * r + a3) * r + a4) * r + a5) * q /
                       (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1);
            }
            else
            {
                // Rational approximation for upper region
                double q = Math.Sqrt(-2 * Math.Log(1 - p));
                return -(((((c0 * q + c1) * q + c2) * q + c3) * q + c4) * q + c5) /
                        ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
        }
    }
}
