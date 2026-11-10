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

        // Dogfood the Chaos expectation oracle: when a chaos run emitted a sidecar, verify the
        // produced Load File against it in the same run. Discovery is limited to eligible chaos
        // runs so a stale sidecar beside a reused output path cannot fail an unrelated run.
        // Normal validation remains skipped for chaos runs.
        if (context.IsChaosMode)
        {
            var expectationValidator = new ChaosExpectationValidator();
            foreach (var (formatName, loadFilePath) in context.LoadFiles)
            {
                var sidecarPath = FindExpectationSidecar(loadFilePath, formatName);
                if (sidecarPath is not null)
                {
                    vr.AddRange(expectationValidator.Validate(loadFilePath, sidecarPath, context.Request).Findings);
                }
            }
        }

        if (vr.HasErrors || vr.HasWarnings)
        {
            Console.Error.WriteLine(vr.GetSummary());
            if (vr.HasErrors)
                throw new InvalidOperationException("Post-generation validation failed.");
        }
    }

    private static string? FindExpectationSidecar(string loadFilePath, string formatName)
    {
        if (string.IsNullOrEmpty(loadFilePath) || !File.Exists(loadFilePath))
            return null;

        var directory = Path.GetDirectoryName(loadFilePath) ?? string.Empty;
        var fileName = Path.GetFileName(loadFilePath);
        var baseName = Path.GetFileName(Path.ChangeExtension(fileName, null));
        // Two Audit File naming conventions exist: <loadfile>_properties.json and <loadfile.ext>_properties.json.
        // Try the format's usual one first, then the other, and require the sidecar's loadFile to match.
        string[] auditNames = string.Equals(formatName, "opt", StringComparison.OrdinalIgnoreCase)
            ? [fileName + "_properties.json", baseName + "_properties.json"]
            : [baseName + "_properties.json", fileName + "_properties.json"];

        foreach (var auditName in auditNames)
        {
            var sidecarPath = LoadFileAuditWriter.ExpectationSidecarPath(Path.Combine(directory, auditName));
            if (File.Exists(sidecarPath) && SidecarNamesLoadFile(sidecarPath, fileName))
                return sidecarPath;
        }

        return null;
    }

    private static bool SidecarNamesLoadFile(string sidecarPath, string loadFileName)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sidecarPath));
            return document.RootElement.TryGetProperty("loadFile", out var loadFile)
                && string.Equals(loadFile.GetString(), loadFileName, StringComparison.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            // Let the validator report the malformed sidecar instead of silently skipping it.
            return true;
        }
    }
}
