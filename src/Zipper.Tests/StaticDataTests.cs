using Xunit;
using Zipper.Profiles;

namespace Zipper;

public class StaticDataTests
{
    [Fact]
    public void LoremIpsum_GetParagraph_ReturnsText()
    {
        // Arrange
        var random = new Random(42);

        // Act
        var paragraph = LoremIpsum.GetParagraph(random);

        // Assert
        Assert.NotNull(paragraph);
        Assert.NotEmpty(paragraph);
    }

    [Fact]
    public void LoremIpsum_GetSentence_ReturnsText()
    {
        // Arrange
        var random = new Random(42);

        // Act
        var sentence = LoremIpsum.GetSentence(random);

        // Assert
        Assert.NotNull(sentence);
        Assert.NotEmpty(sentence);
    }

    [Fact]
    public void EmailSubjects_GetRandom_ReturnsSubject()
    {
        // Arrange
        var random = new Random(42);

        // Act
        var subject = EmailSubjects.GetRandom(random);

        // Assert
        Assert.NotNull(subject);
        Assert.NotEmpty(subject);
    }

    [Fact]
    public void ReviewNotes_GetRandomNote_ReturnsNote()
    {
        // Arrange
        var random = new Random(42);

        // Act
        var note = ReviewNotes.GetRandomNote(random);

        // Assert
        Assert.NotNull(note);
        Assert.NotEmpty(note);
    }

    [Fact]
    public void Names_FirstNames_HasData()
    {
        // Act
        var firstNames = Names.FirstNames;

        // Assert
        Assert.NotNull(firstNames);
        Assert.NotEmpty(firstNames);
    }

    [Fact]
    public void Names_LastNames_HasData()
    {
        // Act
        var lastNames = Names.LastNames;

        // Assert
        Assert.NotNull(lastNames);
        Assert.NotEmpty(lastNames);
    }
}
