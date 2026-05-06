namespace Zipper.Emails;

/// <summary>
/// Context information for generating contextual email templates.
/// </summary>
public record EmailContext
{
    public int RecipientIndex { get; init; }

    public int SenderIndex { get; init; }

    public EmailCategory Category { get; init; }

    public int TemplateIndex { get; init; }

    public DateTime? SentDate { get; init; }

    public bool? IsHighPriority { get; init; }

    public bool? RequestReadReceipt { get; init; }

    public string? RecipientType { get; init; }

    public string? SenderType { get; init; }
}
