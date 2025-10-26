namespace Zipper
{
    /// <summary>
    /// Proportional distribution calculator using round-robin assignment
    /// Provides simple, even distribution of files across folders
    /// </summary>
    public static class ProportionalDistribution
    {
        /// <summary>
        /// Calculates folder number using proportional (round-robin) distribution
        /// </summary>
        /// <param name="fileIndex">Current file index (1-based)</param>
        /// <param name="totalFolders">Total number of folders (1-100)</param>
        /// <returns>Folder number (1 to totalFolders)</returns>
        public static int CalculateFolder(long fileIndex, int totalFolders)
        {
            // Round-robin assignment (existing logic)
            return (int)((fileIndex - 1) % totalFolders) + 1;
        }
    }
}