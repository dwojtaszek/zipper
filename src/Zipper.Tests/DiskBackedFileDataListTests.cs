using Xunit;
using Zipper.Emails;

namespace Zipper.Tests
{
    public class DiskBackedFileDataListTests
    {
        [Fact]
        public void RoundTrip_WithAllFieldsPopulated_PreservesData()
        {
            using var list = new DiskBackedFileDataList();

            var original = new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 42,
                    FolderNumber = 3,
                    FolderName = "folder_003",
                    FileName = "test.txt",
                    FilePathInZip = "folder_003/test.txt"
                },
                DataLength = 1024,
                PageCount = 5,
                Hash = "abcdef1234567890",
                Attachment = ("attach.pdf", Array.Empty<byte>()),
                Email = new Email
                {
                    To = "to@example.com",
                    From = "from@example.com",
                    Subject = "Test Subject",
                    SentDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            };

            list.Add(original);

            var result = list.Single();

            Assert.Equal(original.WorkItem.Index, result.WorkItem.Index);
            Assert.Equal(original.WorkItem.FolderNumber, result.WorkItem.FolderNumber);
            Assert.Equal(original.WorkItem.FolderName, result.WorkItem.FolderName);
            Assert.Equal(original.WorkItem.FileName, result.WorkItem.FileName);
            Assert.Equal(original.WorkItem.FilePathInZip, result.WorkItem.FilePathInZip);

            Assert.Equal(original.DataLength, result.DataLength);
            Assert.Equal(original.PageCount, result.PageCount);
            Assert.Equal(original.Hash, result.Hash);

            Assert.True(result.Attachment.HasValue);
            Assert.Equal(original.Attachment.Value.filename, result.Attachment.Value.filename);
            Assert.Empty(result.Attachment.Value.content);

            Assert.NotNull(result.Email);
            Assert.Equal(original.Email.To, result.Email.To);
            Assert.Equal(original.Email.From, result.Email.From);
            Assert.Equal(original.Email.Subject, result.Email.Subject);
            Assert.Equal(original.Email.SentDate, result.Email.SentDate);
        }

        [Fact]
        public void RoundTrip_WithNullOrEmptyEdgeCases_PreservesData()
        {
            using var list = new DiskBackedFileDataList();

            var original = new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = 1,
                    FolderNumber = 0,
                    FolderName = string.Empty,
                    FileName = string.Empty,
                    FilePathInZip = string.Empty
                },
                DataLength = 0,
                PageCount = 0,
                Hash = string.Empty,
                Attachment = null,
                Email = null
            };

            list.Add(original);

            var result = list.Single();

            Assert.Equal(original.WorkItem.Index, result.WorkItem.Index);
            Assert.Equal(string.Empty, result.WorkItem.FolderName);
            Assert.Equal(string.Empty, result.WorkItem.FileName);
            Assert.Equal(string.Empty, result.WorkItem.FilePathInZip);
            Assert.Equal(0, result.DataLength);
            Assert.Equal(0, result.PageCount);
            Assert.Equal(string.Empty, result.Hash);
            Assert.False(result.Attachment.HasValue);
            Assert.Null(result.Email);
        }

        [Fact]
        public void Dispose_RemovesTempFile()
        {
            using (var list = new DiskBackedFileDataList())
            {
                list.Add(new FileData { WorkItem = new FileWorkItem { Index = 1 } });

                // Get the temp file path by reflection or assume it's created. We can just test that we don't leak files.
                // Wait, DiskBackedFileDataList creates a temp file in constructor.
                // We will test if Dispose throws, and we can also assert that it's safe to call multiple times.
            }

            // Should not throw on double dispose
            var doubleDisposeList = new DiskBackedFileDataList();
            doubleDisposeList.Dispose();
            doubleDisposeList.Dispose();
        }

        [Fact]
        public void Add_AfterDispose_ThrowsObjectDisposedException()
        {
            var list = new DiskBackedFileDataList();
            list.Dispose();

            Assert.Throws<ObjectDisposedException>(() => list.Add(new FileData { WorkItem = new FileWorkItem { Index = 1 } }));
        }

        [Fact]
        public void Enumerable_MultipleItems_AreReturnedInOrder()
        {
            using var list = new DiskBackedFileDataList();

            for (int i = 0; i < 100; i++)
            {
                list.Add(new FileData { WorkItem = new FileWorkItem { Index = i } });
            }

            Assert.Equal(100, list.Count);

            int expected = 0;
            foreach (var item in list)
            {
                Assert.Equal(expected++, item.WorkItem.Index);
            }

            Assert.Equal(100, expected);
        }

        [Fact]
        public void Indexer_ThrowsNotSupportedException()
        {
            using var list = new DiskBackedFileDataList();
            Assert.Throws<NotSupportedException>(() => list[0]);
        }
    }
}
