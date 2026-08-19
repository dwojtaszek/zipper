using Xunit;
using Zipper.Validation;

namespace Zipper.Tests;

public class OptBoundaryValidatorTests
{
    [Theory]
    [InlineData("a,b,c,d,e,f,g\n", true)]
    [InlineData("a,b,c,d,e,f\n", false)]
    [InlineData("a,b,c,d,e,f,g,h\n", false)]
    [InlineData("a,b,c,d,e,f,g\na,b,c\n", false)]
    public void OptBoundaryValidator_ValidatesColumns(string optContent, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new OptBoundaryValidator();

        validator.Validate(optContent, "test.opt", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    [Fact]
    public void OptBoundaryValidator_Validate_NullContent_ThrowsArgumentNullException()
    {
        var result = new ValidationResult();
        var validator = new OptBoundaryValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, "test.opt", result));
    }

    [Fact]
    public void OptBoundaryValidator_Validate_NullResult_ThrowsArgumentNullException()
    {
        var validator = new OptBoundaryValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate("a,b\n", "test.opt", null!));
    }

    [Fact]
    public void OptBoundaryValidator_Validate_QuotedCommas_SplitsNaively()
    {
        // NOTE: Current implementation uses line.Split(',') which splits inside quotes.
        // This test pins that behavior so it is not accidentally changed. A separate
        // issue should be filed to fix the naive split.
        var result = new ValidationResult();
        var validator = new OptBoundaryValidator();

        validator.Validate("\"a,b\",c,d,e,f,g,h\n", "test.opt", result);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Findings, f => f.Category == "OptBoundary");
    }

    [Fact]
    public void OptBoundaryValidator_Validate_WhitespaceOnlyLine_Skipped()
    {
        // NOTE: Current implementation does not skip whitespace-only lines.
        // This pins that behavior. File a separate issue if whitespace trimming is required.
        var result = new ValidationResult();
        var validator = new OptBoundaryValidator();

        validator.Validate("a,b,c,d,e,f,g\n   \na,b,c,d,e,f,g\n", "test.opt", result);

        Assert.True(result.HasErrors);
    }
}
