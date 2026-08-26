using Xunit;
using Zipper.Validation;

namespace Zipper.Tests;

public class ColumnCountValidatorTests
{
    [Theory]
    [InlineData("a,b,c\nd,e,f\n", 3, true)]
    [InlineData("a,b,c\nd,e\n", 3, false)]
    [InlineData("a,b,c\nd,e,f,g\n", 3, false)]
    [InlineData("a,b,c\n\"d,e\",f,g\n", 3, true)]
    [InlineData("a,b,c\nd,e,f,g,h,i\n", 3, false)]
    public void ColumnCountValidator_ValidatesCsvColumns(string csvContent, int expectedColumns, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateCsv(csvContent, expectedColumns, "test.csv", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    [Theory]
    [InlineData("DOCID\u001eFILEPATH\ndoc001\u001efile.pdf\n", 2, true)]
    [InlineData("DOCID\u001eFILEPATH\ndoc001\n", 2, false)]
    public void ColumnCountValidator_ValidatesDatColumns(string datContent, int expectedColumns, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateDat(datContent, expectedColumns, '\x1e', "test.dat", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    [Theory]
    [InlineData("þDOCIDþ\x14þFILEPATHþ\nþdoc001þ\x14þfile.pdfþ\n", true)]
    [InlineData("þDOCIDþ\x14þFILEPATHþ\nþdoc001þ\n", false)]
    public void ColumnCountValidator_ValidatesConcordanceColumns(string content, bool shouldPass)
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateConcordance(content, '\x14', "test.dat", result);

        Assert.Equal(shouldPass, !result.HasErrors);
    }

    [Fact]
    public void ColumnCountValidator_ValidateCsv_NullContent_ThrowsArgumentNullException()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        Assert.Throws<ArgumentNullException>(() => validator.ValidateCsv(null!, 3, "test.csv", result));
    }

    [Fact]
    public void ColumnCountValidator_ValidateCsv_NullResult_ThrowsArgumentNullException()
    {
        var validator = new ColumnCountValidator();

        Assert.Throws<ArgumentNullException>(() => validator.ValidateCsv("a,b\n", 3, "test.csv", null!));
    }

    [Fact]
    public void ColumnCountValidator_ValidateDat_NullContent_ThrowsArgumentNullException()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        Assert.Throws<ArgumentNullException>(() => validator.ValidateDat(null!, 3, '\x1e', "test.dat", result));
    }

    [Fact]
    public void ColumnCountValidator_ValidateDat_NullResult_ThrowsArgumentNullException()
    {
        var validator = new ColumnCountValidator();

        Assert.Throws<ArgumentNullException>(() => validator.ValidateDat("a\x1eb\n", 3, '\x1e', "test.dat", null!));
    }

    [Fact]
    public void ColumnCountValidator_ValidateConcordance_NullContent_ThrowsArgumentNullException()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        Assert.Throws<ArgumentNullException>(() => validator.ValidateConcordance(null!, '\x14', "test.dat", result));
    }

    [Fact]
    public void ColumnCountValidator_ValidateConcordance_NullResult_ThrowsArgumentNullException()
    {
        var validator = new ColumnCountValidator();

        Assert.Throws<ArgumentNullException>(() => validator.ValidateConcordance("þaþ\x14þbþ\n", '\x14', "test.dat", null!));
    }

    [Fact]
    public void ColumnCountValidator_ValidateCsv_EmptyContent_Passes()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateCsv(string.Empty, 3, "test.csv", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ColumnCountValidator_ValidateDat_EmptyContent_Passes()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateDat(string.Empty, 3, '\x1e', "test.dat", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ColumnCountValidator_ValidateConcordance_EmptyContent_ReturnsEarly()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateConcordance(string.Empty, '\x14', "test.dat", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ColumnCountValidator_CountDatColumns_QuoteTogglesOnXfe()
    {
        Assert.Equal(3, ColumnCountValidator.CountDatColumns("aþbþcdþf".AsSpan(), '\x1e'));
    }

    [Fact]
    public void ColumnCountValidator_CountCsvColumns_QuotedEmbeddedDelimiters()
    {
        Assert.Equal(2, ColumnCountValidator.CountCsvColumns("\"a,b\",c".AsSpan()));
    }

    [Fact]
    public void ColumnCountValidator_CountCsvColumns_QuotedEmbeddedDelimitersMultiple()
    {
        Assert.Equal(3, ColumnCountValidator.CountCsvColumns("a,\"b,c\",d".AsSpan()));
    }

    [Fact]
    public void ColumnCountValidator_ValidateCsv_BlankLine_Skipped()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateCsv("a,b,c\n\nd,e,f\n", 3, "test.csv", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ColumnCountValidator_ValidateDat_BlankLine_Skipped()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateDat("a\u001eb\u001ec\n\n d\u001ee\u001ef\n", 3, '\x1e', "test.dat", result);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ColumnCountValidator_ValidateConcordance_BlankLine_Skipped()
    {
        var result = new ValidationResult();
        var validator = new ColumnCountValidator();

        validator.ValidateConcordance("þaþ\x14þbþ\n\nþcþ\x14þdþ\n", '\x14', "test.dat", result);

        Assert.False(result.HasErrors);
    }
}
