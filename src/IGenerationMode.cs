namespace Zipper
{
    /// <summary>
    /// Represents one of the program's generation modes (Standard, Loadfile-Only, Production Set).
    /// Each implementation owns its own config logging, generator invocation, and result printing.
    /// Errors are propagated as exceptions and turned into a non-zero exit code by <see cref="GenerationRunner"/>.
    /// </summary>
    internal interface IGenerationMode
    {
        /// <summary>
        /// Runs the mode end-to-end. Throws on failure; the runner translates exceptions into exit code 1.
        /// </summary>
        Task RunAsync(FileGenerationRequest request);
    }
}
