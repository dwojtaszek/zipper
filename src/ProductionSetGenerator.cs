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
        return await GenerateAsync(request, new ProductionFileMaterializer(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates a complete production set with an injected materializer seam.
    /// </summary>
    internal static async Task<ProductionSetResult> GenerateAsync(
        FileGenerationRequest request,
        IFileMaterializer materializer,
        CancellationToken cancellationToken = default)
    {
        return await ProductionSetOrchestrator.GenerateAsync(
            request,
            materializer,
            new HashComputer(),
            cancellationToken).ConfigureAwait(false);
    }
}
