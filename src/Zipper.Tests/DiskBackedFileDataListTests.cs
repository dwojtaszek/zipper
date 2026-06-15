using Xunit;
using Zipper.Emails;

namespace Zipper.Tests;

public class DiskBackedFileDataListTests
{
    [Fact]
    public void RoundTrip_EmptyList_ReturnsNoElements()
    {
        using var list = new DiskBackedFileDataList();
        Assert.Empty(list);

        var enumerated = list.ToList();
        Assert.Empty(enumerated);
    }

    [Fact]
    public void RoundTrip_SerializeDeserialize_PreservesAllFields()
    {
        using var list = new DiskBackedFileDataList();

        var originalData = new FileData
        {
            WorkItem = new FileWorkItem
            {
                Index = 42,
                FolderNumber = 3,
                FolderName = "folder_003",
                FileName = "00000042.pdf",
                FilePathInZip = "folder_003/00000042.pdf"
            },
            DataLength = 1024,
            PageCount = 5,
            Hash = "abcdef1234567890",
            Attachment = ("test_attach.txt", Array.Empty<byte>()),
            Email = new Email
            {
                To = "to@example.com",
                From = "from@example.com",
                Subject = "Test Subject",
                SentDate = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            }
        };

        list.Add(originalData);

        Assert.Single(list);

        var resultList = list.ToList();
        var deserializedData = Assert.Single(resultList);

        // Compare properties
        Assert.Equal(originalData.WorkItem.Index, deserializedData.WorkItem.Index);
        Assert.Equal(originalData.WorkItem.FolderNumber, deserializedData.WorkItem.FolderNumber);
        Assert.Equal(originalData.WorkItem.FolderName, deserializedData.WorkItem.FolderName);
        Assert.Equal(originalData.WorkItem.FileName, deserializedData.WorkItem.FileName);
        Assert.Equal(originalData.WorkItem.FilePathInZip, deserializedData.WorkItem.FilePathInZip);

        Assert.Equal(originalData.DataLength, deserializedData.DataLength);
        Assert.Equal(originalData.PageCount, deserializedData.PageCount);
        Assert.Equal(originalData.Hash, deserializedData.Hash);

        Assert.True(deserializedData.Attachment.HasValue);
        Assert.Equal(originalData.Attachment.Value.filename, deserializedData.Attachment.Value.filename);

        Assert.NotNull(deserializedData.Email);
        Assert.Equal(originalData.Email.To, deserializedData.Email.To);
        Assert.Equal(originalData.Email.From, deserializedData.Email.From);
        Assert.Equal(originalData.Email.Subject, deserializedData.Email.Subject);
        Assert.Equal(originalData.Email.SentDate, deserializedData.Email.SentDate);
    }

    [Fact]
    public void RoundTrip_NullAndEmptyValues_PreservedProperly()
    {
        using var list = new DiskBackedFileDataList();

        var originalData = new FileData
        {
            WorkItem = new FileWorkItem
            {
                Index = 1,
                FolderNumber = 1,
                FolderName = "",
                FileName = null!,
                FilePathInZip = ""
            },
            DataLength = 0,
            PageCount = 0,
            Hash = null!,
            Attachment = null,
            Email = null
        };

        list.Add(originalData);

        var resultList = list.ToList();
        var deserializedData = Assert.Single(resultList);

        Assert.Equal("", deserializedData.WorkItem.FolderName);
        Assert.Equal("", deserializedData.WorkItem.FileName);
        Assert.Equal("", deserializedData.WorkItem.FilePathInZip);
        Assert.Equal("", deserializedData.Hash);
        Assert.False(deserializedData.Attachment.HasValue);
        Assert.Null(deserializedData.Email);
    }

    [Fact]
    public void Dispose_RemovesTempFile()
    {
        var list = new DiskBackedFileDataList();
        list.Add(new FileData { WorkItem = new FileWorkItem { Index = 1 } });

        var field = typeof(DiskBackedFileDataList).GetField("tempFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var tempFilePath = Assert.IsType<string>(field!.GetValue(list));
        Assert.True(File.Exists(tempFilePath));

        list.Dispose();
        Assert.False(File.Exists(tempFilePath));
        list.Dispose(); // idempotent
    }

    [Fact]
    public void Threshold_ManyItems_ArePersistedAndReadProperly()
    {
        using var list = new DiskBackedFileDataList();

        int itemCount = 10000;
        for (int i = 0; i < itemCount; i++)
        {
            list.Add(new FileData
            {
                WorkItem = new FileWorkItem { Index = i, FileName = $"file_{i}.txt" },
                DataLength = i
            });
        }

        Assert.Equal(itemCount, list.Count);

        int index = 0;
        foreach (var item in list)
        {
            Assert.Equal(index, item.WorkItem.Index);
            Assert.Equal($"file_{index}.txt", item.WorkItem.FileName);
            Assert.Equal(index, item.DataLength);
            index++;
        }

        Assert.Equal(itemCount, index);
    }

    [Fact]
    public void Operations_AfterDispose_ThrowObjectDisposedException()
    {
        var list = new DiskBackedFileDataList();
        list.Dispose();

        Assert.Throws<ObjectDisposedException>(() => list.Add(new FileData()));
        Assert.Throws<ObjectDisposedException>(() => list.ToList());
        Assert.Throws<ObjectDisposedException>(() => list.GetEnumerator());
    }
}
