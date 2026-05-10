namespace Zipper.Profiles.Generation;

/// <summary>Folder-number-based custodian: "Custodian {folderNumber}".</summary>
internal sealed class LegacyFolderCustodianGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => $"Custodian {context.FolderNumber}";
}

/// <summary>Index-based custodian: "Custodian {(index % 10) + 1}".</summary>
internal sealed class LegacyIndexCustodianGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => $"Custodian {(context.NativeFileIndex % 10) + 1}";
}

/// <summary>Legacy DateSent: random date within 365 days before Now.</summary>
internal sealed class LegacyDateSentGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.Now.AddDays(-context.Seeded.Next(1, 365)).ToString("yyyy-MM-dd");
}

/// <summary>Legacy DateCreated: random date within 730 days before Now.</summary>
internal sealed class LegacyDateCreatedGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.Now.AddDays(-context.Seeded.Next(1, 730)).ToString("yyyy-MM-dd");
}

/// <summary>Legacy Author: "Author NNN" from random 1-99.</summary>
internal sealed class LegacyAuthorGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => $"Author {context.Seeded.Next(1, 100):D3}";
}

/// <summary>FileSize from actual FileData.DataLength.</summary>
internal sealed class LegacyFileSizeFromDataGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => (context.FileData?.DataLength ?? 0).ToString();
}

/// <summary>Random FileSize in [1024, 10485760] (loadfile-only mode).</summary>
internal sealed class LegacyRandomFileSizeGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => context.Seeded.Next(1024, 10485760).ToString();
}

/// <summary>EmailTo column: reads fileData.Email.To; falls back to synthetic.</summary>
internal sealed class LegacyEmailToGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.FileData?.Email?.To ?? $"recipient{context.NativeFileIndex}@example.com";
}

/// <summary>EmailFrom column: reads fileData.Email.From; falls back to synthetic.</summary>
internal sealed class LegacyEmailFromGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.FileData?.Email?.From ?? $"sender{context.NativeFileIndex}@example.com";
}

/// <summary>EmailSubject column: reads fileData.Email.Subject; falls back to synthetic.</summary>
internal sealed class LegacyEmailSubjectGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.FileData?.Email?.Subject ?? $"Email Subject {context.NativeFileIndex}";
}

/// <summary>EmailSentDate column: reads fileData.Email.SentDate; falls back to random.</summary>
internal sealed class LegacyEmailSentDateGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.FileData?.Email?.SentDate.ToString("yyyy-MM-dd HH:mm:ss")
            ?? context.Now.AddDays(-context.Seeded.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>EmailAttachment column: reads fileData.Attachment filename.</summary>
internal sealed class LegacyEmailAttachmentGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.FileData?.Attachment.HasValue == true ? context.FileData.Attachment.Value.filename : string.Empty;
}

/// <summary>Synthetic EmailTo: index-based for loadfile-only mode.</summary>
internal sealed class LegacySyntheticEmailToGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => $"recipient{context.NativeFileIndex}@example.com";
}

/// <summary>Synthetic EmailFrom: index-based for loadfile-only mode.</summary>
internal sealed class LegacySyntheticEmailFromGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => $"sender{context.NativeFileIndex}@example.com";
}

/// <summary>Synthetic EmailSubject: index-based for loadfile-only mode.</summary>
internal sealed class LegacySyntheticEmailSubjectGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => $"Email Subject {context.NativeFileIndex}";
}

/// <summary>Synthetic EmailSentDate: random within 30 days of Now.</summary>
internal sealed class LegacySyntheticEmailSentDateGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) =>
        context.Now.AddDays(-context.Seeded.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
}
