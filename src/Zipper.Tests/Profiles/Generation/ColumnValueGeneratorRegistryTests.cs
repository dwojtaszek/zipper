using Xunit;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

public class ColumnValueGeneratorRegistryTests
{
    [Fact]
    public void KnownTypes_ContainsAllProfileDrivenKinds()
    {
        Assert.Contains("identifier", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("text", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("longtext", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("date", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("datetime", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("number", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("boolean", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("coded", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("email", ColumnValueGeneratorRegistry.KnownTypes);
    }

    [Fact]
    public void KnownTypes_ContainsAllLegacyMetadataKinds()
    {
        Assert.Contains("folderCustodian", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("indexCustodian", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("legacyDateSent", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("legacyAuthor", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("fileDataSize", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("randomFileSize", ColumnValueGeneratorRegistry.KnownTypes);
    }

    [Fact]
    public void KnownTypes_ContainsAllEmailColumnKinds()
    {
        Assert.Contains("emailTo", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("emailFrom", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("emailSubject", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("emailSentDate", ColumnValueGeneratorRegistry.KnownTypes);
        Assert.Contains("emailAttachment", ColumnValueGeneratorRegistry.KnownTypes);
    }

    [Fact]
    public void IsKnownType_ReturnsFalseForUnknownType()
    {
        Assert.False(ColumnValueGeneratorRegistry.IsKnownType("unknownType"));
        Assert.False(ColumnValueGeneratorRegistry.IsKnownType(string.Empty));
    }

    [Fact]
    public void IsKnownType_IsCaseInsensitive()
    {
        Assert.True(ColumnValueGeneratorRegistry.IsKnownType("IDENTIFIER"));
        Assert.True(ColumnValueGeneratorRegistry.IsKnownType("Text"));
        Assert.True(ColumnValueGeneratorRegistry.IsKnownType("FOLDERCUSTODIAN"));
    }

    [Fact]
    public void KnownTypes_HasNoUnknownDuplicates()
    {
        // Each entry should be distinct (HashSet enforces uniqueness)
        Assert.Equal(ColumnValueGeneratorRegistry.KnownTypes.Count, ColumnValueGeneratorRegistry.KnownTypes.ToHashSet().Count);
    }
}
