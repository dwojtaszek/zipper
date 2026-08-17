namespace Zipper;

/// <summary>
/// Backward-compatible thin facade over <see cref="ProductionSetOrchestrator"/>.
/// Existing callers keep using <see cref="GenerateAsync(FileGenerationRequest, CancellationToken)"/>
/// while tests and new code can construct the orchestrator directly with injected seams.
/// </summary>
internal static class ProductionSetGenerator
{
    /// <summary>
    /// Generates a complete production set using the real filesystem materializer and shared hash computer.
    /// </summary>
    public static async Task<ProductionSetResult> GenerateAsync(FileGenerationRequest request, CancellationToken cancellationToken = default)
    {
        return await ProductionSetOrchestrator.GenerateAsync(
            request,
            new ProductionFileMaterializer(),
            new HashComputer(),
            cancellationToken).ConfigureAwait(false);
    }
}
