using Xunit;

using Zipper.Config;

namespace Zipper.Tests.Emails;

public class EmlFileGeneratorDeterminismTests
{
    [Fact]
    public void Generate_WithSeed_ProducesByteIdenticalOutput()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 42 },
        };
        var workItem = new FileWorkItem
        {
            Index = 5,
            FileName = "00000005.eml",
            FilePathInZip = "folder_001/00000005.eml",
            FolderName = "folder_001",
            FolderNumber = 1,
        };

        var result1 = generator.Generate(workItem, request);
        var result2 = generator.Generate(workItem, request);

        Assert.Equal(result1.Content, result2.Content);
    }

    [Fact]
    public void Generate_WithSeedAndAttachments_ProducesByteIdenticalOutput()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 10, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 99 },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };
        var workItem = new FileWorkItem
        {
            Index = 3,
            FileName = "00000003.eml",
            FilePathInZip = "folder_001/00000003.eml",
            FolderName = "folder_001",
            FolderNumber = 1,
        };

        var result1 = generator.Generate(workItem, request);
        var result2 = generator.Generate(workItem, request);

        Assert.Equal(result1.Content, result2.Content);
        Assert.Equal(result1.Attachment, result2.Attachment);
    }
}
