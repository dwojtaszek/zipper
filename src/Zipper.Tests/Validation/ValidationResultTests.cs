using Xunit;
using Zipper.Validation;

namespace Zipper.Tests;

public class ValidationResultTests
{
    [Fact]
    public void Result_WithNoFindings_HasNoErrorsOrWarnings()
    {
        var result = new ValidationResult();

        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal("Validation passed: no issues found.", result.GetSummary());
    }

    [Fact]
    public void Result_WithError_HasErrors()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Error, "Test", "error"));

        Assert.True(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void Result_WithWarning_HasWarnings()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Warning, "Test", "warning"));

        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void Result_GetSummary_GroupsByCategory()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e1"));
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e2"));
        result.Add(new ValidationFinding(ValidationSeverity.Warning, "CatB", "w1"));

        var summary = result.GetSummary();

        Assert.Contains("CatA: 2 error(s), 0 warning(s)", summary, StringComparison.Ordinal);
        Assert.Contains("CatB: 0 error(s), 1 warning(s)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_AddRange_AddsAllFindings()
    {
        var result = new ValidationResult();
        var findings = new[]
        {
            new ValidationFinding(ValidationSeverity.Error, "CatA", "e1"),
            new ValidationFinding(ValidationSeverity.Warning, "CatB", "w1"),
        };

        result.AddRange(findings);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Findings, f => f.Severity == ValidationSeverity.Error);
        Assert.Single(result.Findings, f => f.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Result_TotalCount_ReflectsMixedErrorsAndWarnings()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e1"));
        result.Add(new ValidationFinding(ValidationSeverity.Warning, "CatB", "w1"));
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e2"));

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.ErrorCount);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void Result_GetSummary_ContainsMixedErrorAndWarningTotals()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e1"));
        result.Add(new ValidationFinding(ValidationSeverity.Warning, "CatA", "w1"));

        var summary = result.GetSummary();

        Assert.Contains("CatA: 1 error(s), 1 warning(s)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_GetSummary_OrderIsStableAcrossCategories()
    {
        var result = new ValidationResult();
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatB", "e1"));
        result.Add(new ValidationFinding(ValidationSeverity.Error, "CatA", "e2"));

        var summary = result.GetSummary();

        // GroupBy preserves insertion order of first-seen keys, so CatB appears before CatA.
        Assert.StartsWith("CatB", summary, StringComparison.Ordinal);
    }
}
