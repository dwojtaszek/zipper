namespace Zipper
{
    /// <summary>
    /// Represents attachment information for emails.
    /// </summary>
    public record AttachmentInfo
    {
        public string FileName { get; init; } = string.Empty;

        public byte[] Content { get; init; } = Array.Empty<byte>();

        public string? ContentType { get; init; }

        public string? ContentId { get; init; }

        public bool IsInline { get; init; } = false;
    }
}
