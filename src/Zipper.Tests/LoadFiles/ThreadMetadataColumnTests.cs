using Xunit;
using Zipper.Config;
using Zipper.Emails;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

public class ThreadMetadataColumnTests
{
    [Fact]
    public void DatStandardComposer_WithoutThreading_OmitsThreadColumns()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 2 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = false },
        };

        var composer = new DatComposer(request, WriterMode.Standard);
        var headers = composer.HeaderColumns;

        Assert.DoesNotContain("Thread ID", headers);
        Assert.DoesNotContain("Inclusive", headers);
        Assert.DoesNotContain("Thread Parent", headers);
    }

    [Fact]
    public void DatStandardComposer_WithThreading_IncludesThreadColumnsAndValues()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 3 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = true },
        };

        var composer = new DatComposer(request, WriterMode.Standard);
        var headers = composer.HeaderColumns;

        Assert.Contains("Thread ID", headers);
        Assert.Contains("Inclusive", headers);
        Assert.Contains("Thread Parent", headers);

        int threadIdIdx = headers.ToList().IndexOf("Thread ID");
        int inclusiveIdx = headers.ToList().IndexOf("Inclusive");
        int threadParentIdx = headers.ToList().IndexOf("Thread Parent");

        var plan = EmailThreadPlan.GetOrCreate(request);
        var files = new List<FileData>();
        for (long i = 1; i <= 3; i++)
        {
            var node = plan.GetNode(i);
            files.Add(new FileData
            {
                WorkItem = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FilePathInZip = $"NATIVES/{i:D8}.eml" },
                Email = plan.CreateEmail(node),
            });
        }

        var records = composer.Compose(files).ToList();
        Assert.Equal(3, records.Count);

        var firstRecord = records[0];
        Assert.Equal("DOC00000001", firstRecord.Values[threadIdIdx]);
        Assert.Equal(string.Empty, firstRecord.Values[threadParentIdx]);
        Assert.Contains(firstRecord.Values[inclusiveIdx], new[] { "Y", "N" });
    }

    [Fact]
    public void DatStandardComposer_WithThreadingAndFamilies_ChildAttachmentRowsHaveEmptyThreadFields()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = true, WithFamilies = true },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };

        var composer = new DatComposer(request, WriterMode.Standard);
        var headers = composer.HeaderColumns;

        int threadIdIdx = headers.ToList().IndexOf("Thread ID");
        int inclusiveIdx = headers.ToList().IndexOf("Inclusive");
        int threadParentIdx = headers.ToList().IndexOf("Thread Parent");

        var plan = EmailThreadPlan.GetOrCreate(request);
        var node = plan.GetNode(1);
        var email = plan.CreateEmail(node);

        var files = new List<FileData>
        {
            new FileData
            {
                WorkItem = new FileWorkItem { Index = 1, FileName = "00000001.eml", FilePathInZip = "NATIVES/00000001.eml" },
                Attachment = ("attachment.pdf", new byte[] { 1, 2, 3 }),
                AttachmentLength = 3,
                Email = email,
            }
        };

        var records = composer.Compose(files).ToList();
        Assert.Equal(2, records.Count);

        var parentRecord = records[0];
        Assert.Equal("DOC00000001", parentRecord.Values[threadIdIdx]);

        var childRecord = records[1];
        Assert.Equal(string.Empty, childRecord.Values[threadIdIdx]);
        Assert.Equal(string.Empty, childRecord.Values[inclusiveIdx]);
        Assert.Equal(string.Empty, childRecord.Values[threadParentIdx]);
    }

    [Fact]
    public void CsvComposer_WithThreading_EmitsUppercaseHeaders()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = true },
        };

        var composer = new CsvComposer(request);
        var headers = composer.HeaderColumns;

        Assert.Contains("THREAD ID", headers);
        Assert.Contains("INCLUSIVE", headers);
        Assert.Contains("THREAD PARENT", headers);
    }

    [Fact]
    public void ConcordanceComposer_WithThreading_EmitsConcordanceHeaders()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = true },
        };

        var composer = new ConcordanceComposer(request);
        var headers = composer.HeaderColumns;

        Assert.Contains("THREAD_ID", headers);
        Assert.Contains("INCLUSIVE", headers);
        Assert.Contains("THREAD_PARENT", headers);
    }

    [Fact]
    public void DatLoadfileOnlyComposer_WithThreading_EmitsThreadColumns()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 3 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = true },
            LoadfileOnly = true,
        };

        var composer = new DatComposer(request, WriterMode.LoadfileOnly);
        var headers = composer.HeaderColumns;

        Assert.Contains("Thread ID", headers);
        Assert.Contains("Inclusive", headers);
        Assert.Contains("Thread Parent", headers);

        var records = composer.Compose(Array.Empty<FileData>()).ToList();
        Assert.Equal(3, records.Count);
        int threadIdIdx = headers.ToList().IndexOf("Thread ID");
        Assert.Equal("DOC00000001", records[0].Values[threadIdIdx]);
    }

    [Fact]
    public void DatProductionComposer_WithThreading_EmitsThreadColumns()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = true },
            Production = new ProductionConfig { ProductionSet = true },
        };

        var composer = new DatComposer(request, WriterMode.ProductionSet);
        var headers = composer.HeaderColumns;

        Assert.Contains("Thread ID", headers);
        Assert.Contains("Inclusive", headers);
        Assert.Contains("Thread Parent", headers);
    }
}
