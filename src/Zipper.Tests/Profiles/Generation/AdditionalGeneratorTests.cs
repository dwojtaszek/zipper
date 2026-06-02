using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

public class AdditionalGeneratorTests
{
    private ColumnGenerationContext CreateContext()
    {
        return new ColumnGenerationContext
        {
            NativeFileIndex = 5,
            FolderNumber = 2,
            DocumentIndex = 12,
            Seeded = new Random(42),
            Now = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            FileData = new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 5,
                    FolderNumber = 2,
                    FileName = "doc5.pdf",
                    FilePathInZip = "NATIVES/Custodian_2/doc5.pdf"
                },
                DataLength = 2048,
                PageCount = 4
            }
        };
    }

    [Fact]
    public void BooleanGenerator_WithDifferentFormats_ReturnsExpectedRepresentation()
    {
        // Test YN format
        var colYn = new ColumnDefinition { TruePercentage = 100, Format = "yn" };
        var genYn = new BooleanGenerator(colYn);
        Assert.Equal("Y", genYn.Generate(this.CreateContext()));

        var colYnFalse = new ColumnDefinition { TruePercentage = 0, Format = "yn" };
        var genYnFalse = new BooleanGenerator(colYnFalse);
        Assert.Equal("N", genYnFalse.Generate(this.CreateContext()));

        // Test TrueFalse format
        var colTf = new ColumnDefinition { TruePercentage = 100, Format = "truefalse" };
        var genTf = new BooleanGenerator(colTf);
        Assert.Equal("True", genTf.Generate(this.CreateContext()));

        // Test 10 format
        var col10 = new ColumnDefinition { TruePercentage = 100, Format = "10" };
        var gen10 = new BooleanGenerator(col10);
        Assert.Equal("1", gen10.Generate(this.CreateContext()));
    }

    [Fact]
    public void NumberGenerator_WithDifferentColumns_ReturnsExpectedValues()
    {
        var context = this.CreateContext();

        // FILESIZE column
        var colSize = new ColumnDefinition { Name = "FILESIZE" };
        var genSize = new NumberGenerator(colSize);
        Assert.Equal("2048", genSize.Generate(context));

        // PAGECOUNT column
        var colPage = new ColumnDefinition { Name = "PAGECOUNT" };
        var genPage = new NumberGenerator(colPage);
        Assert.Equal("4", genPage.Generate(context));

        // Random range
        var colRange = new ColumnDefinition
        {
            Name = "RandomNum",
            Range = new RangeConfig { Min = 100, Max = 200 }
        };
        var genRange = new NumberGenerator(colRange);
        var val = int.Parse(genRange.Generate(context));
        Assert.InRange(val, 100, 200);

        // Exponential distribution
        var colExp = new ColumnDefinition
        {
            Name = "ExpNum",
            Distribution = "exponential",
            Range = new RangeConfig { Min = 50, Max = 150 }
        };
        var genExp = new NumberGenerator(colExp);
        var expVal = int.Parse(genExp.Generate(context));
        Assert.InRange(expVal, 50, 150);
    }

    [Fact]
    public void EmailAddressGenerator_GeneratesSingleAndMultiValueEmails()
    {
        var context = this.CreateContext();
        var settings = new ProfileSettings { MultiValueDelimiter = ";" };

        // Single value
        var colSingle = new ColumnDefinition { MultiValue = false };
        var genSingle = new EmailAddressGenerator(colSingle, settings);
        var email = genSingle.Generate(context);
        Assert.Contains("@", email);

        // Multi value
        var colMulti = new ColumnDefinition
        {
            MultiValue = true,
            MultiValueCount = new RangeConfig { Min = 2, Max = 2 }
        };
        var genMulti = new EmailAddressGenerator(colMulti, settings);
        var emails = genMulti.Generate(context);
        var parts = emails.Split(';');
        Assert.Equal(2, parts.Length);
        Assert.Contains("@", parts[0]);
        Assert.Contains("@", parts[1]);
    }

    [Fact]
    public void IdentifierGenerator_ProducesExpectedFormat()
    {
        var gen = new IdentifierGenerator();
        var context = this.CreateContext();
        Assert.Equal("DOC00000005", gen.Generate(context));
    }

    [Fact]
    public void LegacyMetadataGenerators_ProduceCorrectValues()
    {
        var context = this.CreateContext();

        Assert.Equal("Custodian 2", new LegacyFolderCustodianGenerator().Generate(context));
        Assert.Equal("Custodian 6", new LegacyIndexCustodianGenerator().Generate(context));

        var dateSent = new LegacyDateSentGenerator().Generate(context);
        Assert.NotNull(dateSent);

        var dateCreated = new LegacyDateCreatedGenerator().Generate(context);
        Assert.NotNull(dateCreated);

        Assert.StartsWith("Author ", new LegacyAuthorGenerator().Generate(context));
        Assert.Equal("2048", new LegacyFileSizeFromDataGenerator().Generate(context));
        Assert.NotEmpty(new LegacyRandomFileSizeGenerator().Generate(context));

        Assert.Equal("recipient5@example.com", new LegacyEmailToGenerator().Generate(context));
        Assert.Equal("sender5@example.com", new LegacyEmailFromGenerator().Generate(context));
        Assert.Equal("Email Subject 5", new LegacyEmailSubjectGenerator().Generate(context));
        Assert.NotEmpty(new LegacyEmailSentDateGenerator().Generate(context));
        Assert.Equal(string.Empty, new LegacyEmailAttachmentGenerator().Generate(context));

        Assert.Equal("recipient5@example.com", new LegacySyntheticEmailToGenerator().Generate(context));
        Assert.Equal("sender5@example.com", new LegacySyntheticEmailFromGenerator().Generate(context));
        Assert.Equal("Email Subject 5", new LegacySyntheticEmailSubjectGenerator().Generate(context));
        Assert.NotEmpty(new LegacySyntheticEmailSentDateGenerator().Generate(context));
    }

    [Fact]
    public void LongTextGenerator_GeneratesLoremIpsumAndReviewNotes()
    {
        var context = this.CreateContext();

        // Review note generator
        var colNote = new ColumnDefinition { Generator = "reviewNote" };
        var genNote = new LongTextGenerator(colNote);
        var note = genNote.Generate(context);
        Assert.NotEmpty(note);

        // Paragraphs generator
        var colPara = new ColumnDefinition
        {
            GeneratorParams = new Dictionary<string, object> { { "min", 2 }, { "max", 2 } }
        };
        var genPara = new LongTextGenerator(colPara);
        var text = genPara.Generate(context);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void TextGenerator_ResolvesSpecificColumnsAndDataSource()
    {
        var context = this.CreateContext();

        // FILEPATH
        var colPath = new ColumnDefinition { Name = "FILEPATH" };
        var genPath = new TextGenerator(colPath, null, null);
        Assert.Equal("NATIVES/Custodian_2/doc5.pdf", genPath.Generate(context));

        // FILENAME
        var colName = new ColumnDefinition { Name = "FILENAME" };
        var genName = new TextGenerator(colName, null, null);
        Assert.Equal("doc5.pdf", genName.Generate(context));

        // FILEEXT
        var colExt = new ColumnDefinition { Name = "FILEEXT" };
        var genExt = new TextGenerator(colExt, null, null);
        Assert.Equal("pdf", genExt.Generate(context));

        // Custom datasource list
        var colDs = new ColumnDefinition { Name = "CustomDs" };
        var dsValues = new[] { "A", "B", "C" };
        var genDs = new TextGenerator(colDs, dsValues, null);
        Assert.Contains(genDs.Generate(context), dsValues);

        // Custom named generator
        var colGen = new ColumnDefinition { Name = "EncodingCol", Generator = "encoding" };
        var genNamed = new TextGenerator(colGen, null, null);
        Assert.NotEmpty(genNamed.Generate(context));
    }
}
