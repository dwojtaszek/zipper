namespace Zipper.LoadFiles;

/// <summary>
/// Produces the synthetic metadata and email column values shared by the CSV and Concordance
/// composers. The random draw order (date sent, author, then conditional sent date) is fixed
/// so output stays deterministic and byte-identical to the legacy writers.
/// </summary>
internal static class SyntheticRowValues
{
    internal static (string Custodian, string DateSent, string Author, string FileSize) Metadata(
        FileWorkItem workItem,
        FileData fileData,
        Random random,
        DateTime now)
    {
        var custodian = $"Custodian {workItem.FolderNumber}";
        var dateSent = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var author = $"Author {random.Next(1, 100):D3}";
        var fileSize = fileData.DataLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (custodian, dateSent, author, fileSize);
    }

    internal static (string To, string From, string Subject, string SentDate, string Attachment) Eml(
        FileWorkItem workItem,
        FileData fileData,
        Random random,
        DateTime now)
    {
        var to = fileData.Email?.To ?? $"recipient{workItem.Index}@example.com";
        var from = fileData.Email?.From ?? $"sender{workItem.Index}@example.com";
        var subject = fileData.Email?.Subject ?? $"Email Subject {workItem.Index}";
        var sentDate = fileData.Email?.SentDate.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            ?? now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var attachment = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : string.Empty;
        return (to, from, subject, sentDate, attachment);
    }

    internal static (string DataSource, string CollectionDate, string DeNisted, string DedupeGroupId, string ProcessingStatus) CollectionMetadata(
        FileWorkItem workItem,
        Random random,
        DateTime now)
    {
        var dataSource = CollectionMetadataValues.DataSources[random.Next(CollectionMetadataValues.DataSources.Length)];
        var collectionDate = now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var deNisted = random.Next(100) < 85 ? "YES" : "NO";
        var dedupeGroupId = $"GRP{random.Next(1, 1000):D6}";
        var processingStatus = CollectionMetadataValues.ProcessingStatuses[random.Next(CollectionMetadataValues.ProcessingStatuses.Length)];
        return (dataSource, collectionDate, deNisted, dedupeGroupId, processingStatus);
    }
}
