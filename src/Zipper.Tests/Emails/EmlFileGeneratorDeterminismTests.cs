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

        // Correctness assertions (M3)
        var emlText = System.Text.Encoding.UTF8.GetString(result1.Content);
        Assert.Contains("To: recipient005@", emlText, StringComparison.Ordinal);
        Assert.Contains("From: sender005@", emlText, StringComparison.Ordinal);
        Assert.Contains("Subject: ", emlText, StringComparison.Ordinal);
        Assert.Contains("MIME-Version: 1.0", emlText, StringComparison.Ordinal);
        Assert.Contains("Date: ", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain", emlText, StringComparison.Ordinal);
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

        // Correctness assertions (M3)
        var emlText = System.Text.Encoding.UTF8.GetString(result1.Content);
        Assert.Contains("To: recipient003@", emlText, StringComparison.Ordinal);
        Assert.Contains("From: sender003@", emlText, StringComparison.Ordinal);
        Assert.Contains("Subject: ", emlText, StringComparison.Ordinal);
        Assert.Contains("MIME-Version: 1.0", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: multipart/mixed", emlText, StringComparison.Ordinal);
        Assert.NotNull(result1.Attachment);
        Assert.NotEmpty(result1.Attachment.Value.filename);
        Assert.NotEmpty(result1.Attachment.Value.content);
    }
    [Fact]
    public void Generate_WithSeed_ProducesValidRfc2822Headers()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 42 },
        };
        var workItem = new FileWorkItem { Index = 1, FileName = "test.eml", FolderName = "f", FolderNumber = 1 };
        var result = generator.Generate(workItem, request);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Headers presence check
        Assert.Contains("From: ", emlText, StringComparison.Ordinal);
        Assert.Contains("To: ", emlText, StringComparison.Ordinal);
        Assert.Contains("Subject: ", emlText, StringComparison.Ordinal);

        // Date should be RFC 2822 formatted with InvariantCulture
        Assert.Matches(@"Date: (Mon|Tue|Wed|Thu|Fri|Sat|Sun), \d{2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}", emlText);
    }
}
