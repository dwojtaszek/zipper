using System.Collections;
using System.Text;
using Zipper.Emails;

namespace Zipper;

internal sealed class DiskBackedFileDataList : IReadOnlyList<FileData>, IDisposable
{
    private readonly string tempFilePath;
    private FileStream? writeStream;
    private BinaryWriter? writer;
    private int count;
    private readonly Lock syncRoot = new();

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
            ObjectDisposedException.ThrowIf(this.writer is null, this);
            this.writer?.Flush();
            this.writeStream?.Flush(true);
        }

        return GetEnumeratorCore();
    }

    private IEnumerator<FileData> GetEnumeratorCore()
    {
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

/// <summary>
/// Serializes metadata-only projections of <see cref="FileData"/> for temporary disk-backed spooling.
/// Heavy payload byte arrays (Native file content, Attachment content, and Email Body) are
/// intentionally omitted to minimize memory consumption and spooling disk I/O.
/// Downstream consumers (load file composers, audit writers, manifest writers) consume only
/// metadata properties (<see cref="FileData.DataLength"/>), Attachment filenames, lengths and
/// requested hashes, and Email address/subject/date fields.
/// </summary>
internal static class FileDataSerializer
{
    public static void Serialize(BinaryWriter writer, FileData data)
    {
        writer.Write(data.WorkItem.Index);
        writer.Write(data.WorkItem.FolderNumber);
        writer.Write(data.WorkItem.FolderName ?? string.Empty);
        writer.Write(data.WorkItem.FileName ?? string.Empty);
        writer.Write(data.WorkItem.FilePathInZip ?? string.Empty);
        writer.Write(data.WorkItem.FileType ?? string.Empty);
        writer.Write(data.DataLength);
        writer.Write(data.PageCount);
        writer.Write(data.Hash ?? string.Empty);

        if (data.Hashes is not null)
        {
            writer.Write(true);
            writer.Write(data.Hashes.Count);
            foreach (var kvp in data.Hashes)
            {
                writer.Write((int)kvp.Key);
                writer.Write(kvp.Value);
            }
        }
        else
        {
            writer.Write(false);
        }

        var attachmentMetadata = data.GetAttachmentMetadata();
        writer.Write(attachmentMetadata.Count);
        foreach (var attachment in attachmentMetadata)
        {
            writer.Write(attachment.FileName ?? string.Empty);
            writer.Write(attachment.Length);
            writer.Write(attachment.Hashes is not null);
            if (attachment.Hashes is not null)
            {
                writer.Write(attachment.Hashes.Count);
                foreach (var (algorithm, hash) in attachment.Hashes)
                {
                    writer.Write((int)algorithm);
                    writer.Write(hash);
                }
            }
        }

        writer.Write(data.Email is not null);
        if (data.Email is not null)
        {
            writer.Write(data.Email.To ?? string.Empty);
            writer.Write(data.Email.From ?? string.Empty);
            writer.Write(data.Email.Subject ?? string.Empty);
            writer.Write(data.Email.SentDate.Ticks);
            writer.Write(data.Email.Cc is not null);
            if (data.Email.Cc is not null)
            {
                writer.Write(data.Email.Cc);
            }

            writer.Write(data.Email.MessageId is not null);
            if (data.Email.MessageId is not null)
            {
                writer.Write(data.Email.MessageId);
            }

            writer.Write(data.Email.InReplyTo is not null);
            if (data.Email.InReplyTo is not null)
            {
                writer.Write(data.Email.InReplyTo);
            }

            writer.Write(data.Email.References is not null);
            if (data.Email.References is not null)
            {
                writer.Write(data.Email.References);
            }

            writer.Write(data.Email.ThreadId is not null);
            if (data.Email.ThreadId is not null)
            {
                writer.Write(data.Email.ThreadId);
            }

            writer.Write(data.Email.IsInclusive.HasValue);
            if (data.Email.IsInclusive.HasValue)
            {
                writer.Write(data.Email.IsInclusive.Value);
            }

            writer.Write(data.Email.ThreadParentId is not null);
            if (data.Email.ThreadParentId is not null)
            {
                writer.Write(data.Email.ThreadParentId);
            }
        }

        writer.Write(data.WorkItem.ControlNumberOverride ?? string.Empty);
        writer.Write(data.WorkItem.BatesNumberOverride ?? string.Empty);

        if (data.WorkItem.SourceMetadata is not null)
        {
            writer.Write(true);
            writer.Write(data.WorkItem.SourceMetadata.Count);
            foreach (var kvp in data.WorkItem.SourceMetadata)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
        }
        else
        {
            writer.Write(false);
        }
    }

    public static FileData Deserialize(BinaryReader reader)
    {
        var index = reader.ReadInt64();
        var folderNumber = reader.ReadInt32();
        var folderName = reader.ReadString();
        var fileName = reader.ReadString();
        var filePathInZip = reader.ReadString();
        var fileType = reader.ReadString();
        var dataLength = reader.ReadInt32();
        var pageCount = reader.ReadInt32();
        var hash = reader.ReadString();

        IReadOnlyDictionary<Config.HashAlgorithm, string>? hashes = null;
        if (reader.ReadBoolean())
        {
            var count = reader.ReadInt32();
            var dict = new Dictionary<Config.HashAlgorithm, string>(count);
            for (int i = 0; i < count; i++)
            {
                var algo = (Config.HashAlgorithm)reader.ReadInt32();
                var value = reader.ReadString();
                dict[algo] = value;
            }

            hashes = dict;
        }

        var attachmentCount = reader.ReadInt32();
        var attachmentMetadata = attachmentCount == 0
            ? Array.Empty<FileAttachmentMetadata>()
            : new FileAttachmentMetadata[attachmentCount];
        for (int i = 0; i < attachmentCount; i++)
        {
            var attFilename = reader.ReadString();
            int attachmentLength = reader.ReadInt32();
            IReadOnlyDictionary<Config.HashAlgorithm, string>? attachmentHashes = null;
            if (reader.ReadBoolean())
            {
                var hashCount = reader.ReadInt32();
                var attHashesDict = new Dictionary<Config.HashAlgorithm, string>(hashCount);
                for (int hashIndex = 0; hashIndex < hashCount; hashIndex++)
                {
                    var algorithm = (Config.HashAlgorithm)reader.ReadInt32();
                    attHashesDict[algorithm] = reader.ReadString();
                }

                attachmentHashes = attHashesDict;
            }

            attachmentMetadata[i] = new FileAttachmentMetadata(attFilename, attachmentLength, attachmentHashes);
        }
        (string filename, byte[] content)? attachment = attachmentMetadata.Length > 0
            ? (attachmentMetadata[0].FileName, Array.Empty<byte>())
            : null;

        Email? email = null;
        if (reader.ReadBoolean())
        {
            var to = reader.ReadString();
            var from = reader.ReadString();
            var subject = reader.ReadString();
            var sentDateTicks = reader.ReadInt64();
            string? cc = null;
            if (reader.ReadBoolean())
            {
                cc = reader.ReadString();
            }

            string? messageId = null;
            if (reader.ReadBoolean())
            {
                messageId = reader.ReadString();
            }

            string? inReplyTo = null;
            if (reader.ReadBoolean())
            {
                inReplyTo = reader.ReadString();
            }

            string? references = null;
            if (reader.ReadBoolean())
            {
                references = reader.ReadString();
            }

            string? threadId = null;
            if (reader.ReadBoolean())
            {
                threadId = reader.ReadString();
            }

            bool? isInclusive = null;
            if (reader.ReadBoolean())
            {
                isInclusive = reader.ReadBoolean();
            }

            string? threadParentId = null;
            if (reader.ReadBoolean())
            {
                threadParentId = reader.ReadString();
            }

            email = new Email
            {
                To = to,
                From = from,
                Subject = subject,
                SentDate = new DateTime(sentDateTicks, DateTimeKind.Utc),
                Cc = cc,
                MessageId = messageId,
                InReplyTo = inReplyTo,
                References = references,
                ThreadId = threadId,
                IsInclusive = isInclusive,
                ThreadParentId = threadParentId,
            };
        }

        var controlNumberOverride = reader.ReadString();
        var batesNumberOverride = reader.ReadString();

        IReadOnlyDictionary<string, string>? sourceMetadata = null;
        if (reader.ReadBoolean())
        {
            var metadataCount = reader.ReadInt32();
            var metadata = new Dictionary<string, string>(metadataCount, StringComparer.Ordinal);
            for (int i = 0; i < metadataCount; i++)
            {
                metadata[reader.ReadString()] = reader.ReadString();
            }

            sourceMetadata = metadata;
        }

        return new FileData
        {
            WorkItem = new FileWorkItem
            {
                Index = index,
                FolderNumber = folderNumber,
                FolderName = folderName,
                FileName = fileName,
                FilePathInZip = filePathInZip,
                FileType = fileType,
                ControlNumberOverride = controlNumberOverride.Length > 0 ? controlNumberOverride : null,
                BatesNumberOverride = batesNumberOverride.Length > 0 ? batesNumberOverride : null,
                SourceMetadata = sourceMetadata
            },
            DataLength = dataLength,
            PageCount = pageCount,
            Hash = hash,
            Hashes = hashes,
            Attachment = attachment,
            AttachmentLength = attachmentMetadata.Length > 0 ? attachmentMetadata[0].Length : 0,
            AttachmentMetadata = attachmentMetadata,
            Email = email
        };
    }
}
