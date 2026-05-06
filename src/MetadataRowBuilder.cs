using System.Text;

using Zipper.Emails;

namespace Zipper;

internal class MetadataRowBuilder
{
    private readonly Random random;
    private readonly DateTime now;
    private readonly FileGenerationRequest request;

    public MetadataRowBuilder(FileGenerationRequest request, Random random, DateTime now)
    {
        this.request = request;
        this.random = random;
        this.now = now;
    }

    public string GetControlNumber(FileWorkItem workItem) => $"DOC{workItem.Index:D8}";

    public string GetControlNumber(long index) => $"DOC{index:D8}";

    public string GetFileSize(FileData fileData) => fileData.DataLength.ToString();

    public string GetFileSize(long actualBytes) => actualBytes.ToString();

    public string GetFileSize()
    {
        return this.random.Next(1024, 10485760).ToString();
    }

    public string GetCustodian(int folderNumber) => $"Custodian {folderNumber}";

    public string GetCustodianByIndex(long index) => $"Custodian {(index % 10) + 1}";

    public string GetCustodian()
    {
        var maxCustodians = Math.Max(2, this.request.Metadata.CustodianCountOverride ?? 10);
        return $"Custodian {this.random.Next(1, maxCustodians + 1)}";
    }

    public string GetDateSent()
    {
        return this.now.AddDays(-this.random.Next(1, 365)).ToString("yyyy-MM-dd");
    }

    public string GetDateCreated()
    {
        return this.now.AddDays(-this.random.Next(1, 730)).ToString("yyyy-MM-dd");
    }

    public string GetAuthor()
    {
        return $"Author {this.random.Next(1, 100):D3}";
    }

    public string GetEmailTo(FileWorkItem workItem, FileData fileData)
    {
        return fileData.Email?.To ?? $"recipient{workItem.Index}@example.com";
    }

    public string GetEmailTo(FileWorkItem workItem)
    {
        return $"recipient{workItem.Index}@example.com";
    }

    public string GetEmailFrom(FileWorkItem workItem, FileData fileData)
    {
        return fileData.Email?.From ?? $"sender{workItem.Index}@example.com";
    }

    public string GetEmailFrom(FileWorkItem workItem)
    {
        return $"sender{workItem.Index}@example.com";
    }

    public string GetEmailSubject(FileWorkItem workItem, FileData fileData)
    {
        return fileData.Email?.Subject ?? $"Email Subject {workItem.Index}";
    }

    public string GetEmailSubject(FileWorkItem workItem)
    {
        return $"Email Subject {workItem.Index}";
    }

    public string GetEmailSentDate(FileWorkItem workItem, FileData fileData)
    {
        return fileData.Email?.SentDate.ToString("yyyy-MM-dd HH:mm:ss") ?? this.now.AddDays(-this.random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
    }

    public string GetEmailSentDate(FileWorkItem workItem)
    {
        return this.now.AddDays(-this.random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
    }

    public string GetEmailAttachment(FileData fileData)
    {
        return fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : string.Empty;
    }

    public string GetPageCount(FileData fileData) => fileData.PageCount.ToString();

    public string GetTextPath(FileWorkItem workItem)
    {
        var sourceSuffix = $".{this.request.Output.FileType}";
        return workItem.FilePathInZip.EndsWith(sourceSuffix, StringComparison.OrdinalIgnoreCase)
            ? workItem.FilePathInZip[..^sourceSuffix.Length] + ".txt"
            : workItem.FilePathInZip;
    }

    public string GetBatesNumber(FileWorkItem workItem)
    {
        return this.request.Bates != null ? BatesNumberGenerator.Generate(this.request.Bates, workItem.Index - 1) : string.Empty;
    }

    public static string SanitizeField(string value, string newlineDelimiter)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Replace("\r\n", newlineDelimiter)
                    .Replace("\n", newlineDelimiter)
                    .Replace("\r", newlineDelimiter);
    }

    public static void AppendField(StringBuilder sb, string value, char quote, bool hasQuote)
    {
        if (hasQuote)
        {
            sb.Append(quote);
            sb.Append(value);
            sb.Append(quote);
        }
        else
        {
            sb.Append(value);
        }
    }
}
