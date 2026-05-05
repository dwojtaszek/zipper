namespace Zipper
{
    /// <summary>
    /// Represents email metadata and content for building EML files.
    /// </summary>
    public record EmailTemplate
    {
        public string To { get; init; } = string.Empty;

        public string From { get; init; } = string.Empty;

        public string Subject { get; init; } = string.Empty;

        public DateTime SentDate { get; init; } = DateTime.Now;

        public string Body { get; init; } = string.Empty;

        public string? Cc { get; init; }

        public string? Bcc { get; init; }

        public string? ReplyTo { get; init; }

        public bool IsHighPriority { get; init; } = false;

        public bool RequestReadReceipt { get; init; } = false;
    }
}
