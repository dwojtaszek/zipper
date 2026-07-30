using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class PathSeparatorCorrectnessTests
{
    private static string ValueOf(LoadFileRecord record, string column) =>
        record.Columns.Zip(record.Values).First(p => p.First == column).Second;

    [Fact]
    public void DatComposer_ComposeProduction_ShouldNormalizePathsToBackslashes()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml" },
            Metadata = new MetadataConfig { WithFamilies = true, Seed = 42 },
            Bates = new BatesNumberConfig { Prefix = "DOC", Start = 1, Digits = 8 }
        };

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderName = "Folder/SubFolder",
                    FilePathInZip = "NATIVES/Folder/SubFolder/DOC00000001.eml"
                },
                DataLength = 100,
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 })
            }
        };

        var composer = new DatComposer(request, WriterMode.ProductionSet);
        var records = composer.Compose(files).ToList();

        Assert.Equal(2, records.Count);

        // Parent record assertions
        var parentRecord = records[0];
        var parentNativePath = ValueOf(parentRecord, "NATIVE_PATH");
        var parentTextPath = ValueOf(parentRecord, "TEXT_PATH");
        var parentImagePath = ValueOf(parentRecord, "IMAGE_PATH");

        Assert.Contains("\\", parentNativePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", parentNativePath, StringComparison.Ordinal);
        Assert.Contains("\\", parentTextPath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", parentTextPath, StringComparison.Ordinal);
        Assert.Contains("\\", parentImagePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", parentImagePath, StringComparison.Ordinal);

        // Child record assertions
        var childRecord = records[1];
        var childNativePath = ValueOf(childRecord, "NATIVE_PATH");
        var childTextPath = ValueOf(childRecord, "TEXT_PATH");
        var childImagePath = ValueOf(childRecord, "IMAGE_PATH");

        Assert.Contains("\\", childNativePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", childNativePath, StringComparison.Ordinal);
        Assert.Contains("\\", childTextPath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", childTextPath, StringComparison.Ordinal);
        Assert.Contains("\\", childImagePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", childImagePath, StringComparison.Ordinal);
    }

    [Fact]
    public void OptComposer_ComposeProduction_ShouldNormalizePathsToBackslashes()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml" },
            Metadata = new MetadataConfig { WithFamilies = true, Seed = 42 },
            Bates = new BatesNumberConfig { Prefix = "DOC", Start = 1, Digits = 8 }
        };

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderName = "Folder/SubFolder",
                    FilePathInZip = "NATIVES/Folder/SubFolder/DOC00000001.eml"
                },
                DataLength = 100,
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 })
            }
        };

        var composer = new OptComposer(request, WriterMode.ProductionSet);
        var records = composer.Compose(files).ToList();

        // One record per page. Since page count defaults to 1, we have 1 parent page + 1 attachment page = 2 records.
        Assert.Equal(2, records.Count);

        // Parent record assertions
        var parentRecord = records[0];
        var parentImagePath = ValueOf(parentRecord, "ImagePath");
        Assert.Contains("\\", parentImagePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", parentImagePath, StringComparison.Ordinal);

        // Child record assertions
        var childRecord = records[1];
        var childImagePath = ValueOf(childRecord, "ImagePath");
        Assert.Contains("\\", childImagePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/", childImagePath, StringComparison.Ordinal);
    }
}
