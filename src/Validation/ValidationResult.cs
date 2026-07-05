namespace Zipper.Validation;

public sealed class ValidationResult
{
    private readonly List<ValidationFinding> _findings = [];

    public IReadOnlyList<ValidationFinding> Findings => _findings;

    public bool HasErrors => _findings.Any(f => f.Severity == ValidationSeverity.Error);

    public bool HasWarnings => _findings.Any(f => f.Severity == ValidationSeverity.Warning);

    public int ErrorCount => _findings.Count(f => f.Severity == ValidationSeverity.Error);

    public int WarningCount => _findings.Count(f => f.Severity == ValidationSeverity.Warning);

    public int TotalCount => _findings.Count;

    public void Add(ValidationFinding finding) => _findings.Add(finding);

    public void AddRange(IEnumerable<ValidationFinding> findings) => _findings.AddRange(findings);

    public string GetSummary()
    {
        if (_findings.Count == 0)
            return "Validation passed: no issues found.";

        var parts = new System.Text.StringBuilder();
        bool first = true;
        foreach (var group in _findings.GroupBy(f => f.Category))
        {
            if (!first)
                parts.Append("; ");
            first = false;
            var errors = group.Count(f => f.Severity == ValidationSeverity.Error);
            var warnings = group.Count(f => f.Severity == ValidationSeverity.Warning);
            parts.Append(System.Globalization.CultureInfo.InvariantCulture, $"{group.Key}: {errors} error(s), {warnings} warning(s)");
        }
        return parts.ToString();
    }
}
