using Xunit;
using Zipper.Validation;

namespace Zipper.Tests;

public class LineEndingValidatorTests
{
    [Theory]
    [InlineData("line1\nline2\n", "\n", true)]
    [InlineData("line1\r\nline2\r\n", "\r\n", true)]
    [InlineData("line1\nline2\r\n", "\n", false)]
    [InlineData("line1\r\nline2\n", "\r\n", false)]
    [InlineData("line1\nline2\n", "\r\n", false)]
    public void LineEndingValidator_DetectsInconsistentEol(string content, string expectedEol, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new LineEndingValidator();

        validator.Validate(content, expectedEol, "test.dat", result);

        if (shouldPass)
        {
            Assert.False(result.HasErrors);
        }
        else
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Findings, finding => finding.Severity == ValidationSeverity.Error && finding.Category == "LineEnding");
        }
    }

    [Fact]
    public void LineEndingValidator_Validate_NullResult_ThrowsArgumentNullException()
    {
        var validator = new LineEndingValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate("line1\nline2\n", "\n", "test.dat", null!));
    }

    [Theory]
    [InlineData("line1\nline2\n")]
    [InlineData("line1\r\nline2\r\n")]
    [InlineData("line1\rline2\nline3\r\n")]
    [InlineData("line1\rline2\r\nline3\n")]
    public void Validate_ExpectedCrWithOtherLineEndings_ShouldReportError(string content)
    {
        var result = new ValidationResult();
        new LineEndingValidator().Validate(content, "\r", "test.dat", result);
        Assert.Contains(result.Findings, finding =>
            finding.Severity == ValidationSeverity.Error && finding.Category == "LineEnding");
    }

    [Fact]
    public void LineEndingValidator_Validate_ContentWithLoneCr_FailsWhenExpectedLf()
    {
        var result = new ValidationResult();
        var validator = new LineEndingValidator();

        validator.Validate("line1\rline2\r", "\n", "test.dat", result);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, finding =>
            finding.Severity == ValidationSeverity.Error && finding.Category == "LineEnding");
    }

    [Fact]
    public void LineEndingValidator_Validate_ContentWithLoneCr_PassesWhenExpectedCr()
    {
        var result = new ValidationResult();
        var validator = new LineEndingValidator();

        validator.Validate("line1\rline2\r", "\r", "test.dat", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void LineEndingValidator_Validate_EmptyContent_ReturnsEarly()
    {
        var result = new ValidationResult();
        var validator = new LineEndingValidator();

        validator.Validate(string.Empty, "\n", "test.dat", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void LineEndingValidator_Validate_WhitespaceContent_ReturnsEarly()
    {
        var result = new ValidationResult();
        var validator = new LineEndingValidator();

        validator.Validate("   \t  ", "\n", "test.dat", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void LineEndingValidator_Validate_NullContent_ReturnsEarly()
    {
        var result = new ValidationResult();
        var validator = new LineEndingValidator();

        validator.Validate(null!, "\n", "test.dat", result);

        Assert.False(result.HasErrors);
    }
}
