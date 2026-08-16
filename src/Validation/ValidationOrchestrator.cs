namespace Zipper.Validation;

/// <summary>
/// Shared post-generation validation tail. Each generation mode adapter
/// constructs its own <see cref="ValidationContext"/> (mode-specific fields),
/// then delegates execution and error reporting here so the identical
/// validator-instantiation / summary / failure-throw block lives in one place.
/// </summary>
public static class ValidationOrchestrator
{
    /// <summary>
    /// Runs <see cref="PostGenerationValidator"/> against the supplied context
    /// and throws <see cref="InvalidOperationException"/> if the result contains
    /// any errors.
    /// </summary>
    public static void RunAfterGeneration(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var validator = new PostGenerationValidator();
        var vr = validator.Validate(context);
        if (vr.HasErrors || vr.HasWarnings)
        {
            Console.Error.WriteLine(vr.GetSummary());
            if (vr.HasErrors)
                throw new InvalidOperationException("Post-generation validation failed.");
        }
    }
}
