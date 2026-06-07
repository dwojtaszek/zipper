using System.Collections;
using System.Text;
using Zipper.Emails;

namespace Zipper
{
    internal sealed class DiskBackedFileDataList : IReadOnlyList<FileData>, IDisposable
    {
        private readonly string tempFilePath;
        private FileStream? writeStream;
        private BinaryWriter? writer;
        private int count;
        private readonly object syncRoot = new object();

        public DiskBackedFileDataList()
        {
            this.tempFilePath = Path.Combine(Path.GetTempPath(), "zipper-" + Guid.NewGuid().ToString("N") + ".tmp");
            this.writeStream = new FileStream(this.tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 4096, FileOptions.DeleteOnClose);
            this.writer = new BinaryWriter(this.writeStream, Encoding.UTF8, leaveOpen: true);
        }

        public int Count => this.count;

        public FileData this[int index] => throw new NotSupportedException("DiskBackedFileDataList only supports sequential enumeration.");

        public void Add(FileData data)
        {
            lock (this.syncRoot)
            {
                ObjectDisposedException.ThrowIf(this.writer is null, this);
                FileDataSerializer.Serialize(this.writer, data);
                this.count++;
            }
        }

        public IEnumerator<FileData> GetEnumerator()
        {
            lock (this.syncRoot)
            {
                this.writer?.Flush();
                this.writeStream?.Flush(true);
            }

            using var readStream = new FileStream(this.tempFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            using var reader = new BinaryReader(readStream, Encoding.UTF8);

            for (int i = 0; i < this.count; i++)
            {
                yield return FileDataSerializer.Deserialize(reader);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public void Dispose()
        {
            lock (this.syncRoot)
            {
                this.writer?.Dispose();
                this.writeStream?.Dispose();
                this.writer = null;
                this.writeStream = null;

                try
                {
                    if (File.Exists(this.tempFilePath))
                    {
                        File.Delete(this.tempFilePath);
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }
    }

    internal static class FileDataSerializer
    {
        public static void Serialize(BinaryWriter writer, FileData data)
        {
            writer.Write(data.WorkItem.Index);
            writer.Write(data.WorkItem.FolderNumber);
            writer.Write(data.WorkItem.FolderName ?? string.Empty);
            writer.Write(data.WorkItem.FileName ?? string.Empty);
            writer.Write(data.WorkItem.FilePathInZip ?? string.Empty);
            writer.Write(data.DataLength);
            writer.Write(data.PageCount);
            writer.Write(data.Hash ?? string.Empty);

            writer.Write(data.Attachment.HasValue);
            if (data.Attachment.HasValue)
            {
                writer.Write(data.Attachment.Value.filename ?? string.Empty);
            }

            writer.Write(data.Email != null);
            if (data.Email != null)
            {
                writer.Write(data.Email.To ?? string.Empty);
                writer.Write(data.Email.From ?? string.Empty);
                writer.Write(data.Email.Subject ?? string.Empty);
                writer.Write(data.Email.SentDate.Ticks);
            }
        }

        public static FileData Deserialize(BinaryReader reader)
        {
            var index = reader.ReadInt64();
            var folderNumber = reader.ReadInt32();
            var folderName = reader.ReadString();
            var fileName = reader.ReadString();
            var filePathInZip = reader.ReadString();
            var dataLength = reader.ReadInt32();
            var pageCount = reader.ReadInt32();
            var hash = reader.ReadString();

            (string filename, byte[] content)? attachment = null;
            if (reader.ReadBoolean())
            {
                var attFilename = reader.ReadString();
                attachment = (attFilename, Array.Empty<byte>());
            }

            Email? email = null;
            if (reader.ReadBoolean())
            {
                var to = reader.ReadString();
                var from = reader.ReadString();
                var subject = reader.ReadString();
                var sentDateTicks = reader.ReadInt64();
                email = new Email
                {
                    To = to,
                    From = from,
                    Subject = subject,
                    SentDate = new DateTime(sentDateTicks, DateTimeKind.Utc)
                };
            }

            return new FileData
            {
                WorkItem = new FileWorkItem
                {
                    Index = index,
                    FolderNumber = folderNumber,
                    FolderName = folderName,
                    FileName = fileName,
                    FilePathInZip = filePathInZip
                },
                DataLength = dataLength,
                PageCount = pageCount,
                Hash = hash,
                Attachment = attachment,
                Email = email
            };
        }
    }
}
