namespace Zipper
{
    /// <summary>
    /// Wraps a mode's RunAsync with the shared try/catch and exit-code conversion previously
    /// duplicated across Program.GenerateFiles, Program.RunLoadfileOnly, and Program.RunProductionSet.
    /// </summary>
    internal static class GenerationRunner
    {
        /// <summary>
        /// Runs the mode and returns 0 on success, 1 on any unhandled exception.
        /// Exception messages are written to standard error in the legacy format
        /// (<c>"\nAn error occurred: {message}"</c>).
        /// </summary>
        public static async Task<int> RunAsync(IGenerationMode mode, FileGenerationRequest request)
        {
            try
            {
                await mode.RunAsync(request);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Format("\nAn error occurred: {0}", ex.Message));
                return 1;
            }
        }
    }
}
