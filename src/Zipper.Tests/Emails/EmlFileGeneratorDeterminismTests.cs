using Xunit;

using Zipper.Config;

namespace Zipper.Tests;

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

    [Fact]
    public void Generate_WithoutThreading_PreservesParityWithoutMessageId()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = false },
        };
        var workItem = new FileWorkItem { Index = 1, FileName = "test.eml", FolderName = "f", FolderNumber = 1 };
        var result = generator.Generate(workItem, request);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        Assert.DoesNotContain("Message-ID:", emlText, StringComparison.Ordinal);
        Assert.DoesNotContain("In-Reply-To:", emlText, StringComparison.Ordinal);
        Assert.DoesNotContain("References:", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithThreading_EmitsRfc5322MessageIdAndAncestryHeaders()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 10, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = true },
        };

        var rootItem = new FileWorkItem { Index = 1, FileName = "00000001.eml", FolderName = "f", FolderNumber = 1 };
        var rootResult = generator.Generate(rootItem, request);
        var rootText = System.Text.Encoding.UTF8.GetString(rootResult.Content);

        Assert.Contains("Message-ID: <msg.", rootText, StringComparison.Ordinal);
        Assert.DoesNotContain("In-Reply-To:", rootText, StringComparison.Ordinal);
        Assert.DoesNotContain("References:", rootText, StringComparison.Ordinal);

        // Subsequent item in thread chain
        bool foundReply = false;
        for (long i = 2; i <= 10; i++)
        {
            var item = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var result = generator.Generate(item, request);
            var text = System.Text.Encoding.UTF8.GetString(result.Content);

            Assert.Contains("Message-ID: <msg.", text, StringComparison.Ordinal);
            if (text.Contains("In-Reply-To:", StringComparison.Ordinal))
            {
                Assert.Contains("References:", text, StringComparison.Ordinal);
                foundReply = true;
            }
        }

        Assert.True(foundReply, "At least one reply should be present in a 10-email threaded corpus.");
    }

    [Fact]
    public void Generate_WithThreading_ProducesByteIdenticalOutputForSameSeed()
    {
        var generator = new EmlFileGenerator();
        var request1 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 5, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 777, WithThreading = true },
        };
        var request2 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 5, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 777, WithThreading = true },
        };

        for (long i = 1; i <= 5; i++)
        {
            var item = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var res1 = generator.Generate(item, request1);
            var res2 = generator.Generate(item, request2);

            Assert.Equal(res1.Content, res2.Content);
        }
    }
}
