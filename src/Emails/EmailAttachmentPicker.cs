namespace Zipper;

/// <summary>
/// A reference to a generated native file that can be used as an email attachment.
/// </summary>
/// <param name="Index">Index of the native file within the production set.</param>
/// <param name="FileName">File name to use as the attachment name.</param>
/// <param name="Content">Raw byte content of the file.</param>
/// <param name="ContentType">Optional MIME content type override.</param>
internal record NativeFileReference(long Index, string FileName, byte[] Content, string? ContentType = null);

/// <summary>
/// Picks an attachment from a pool of native files based on attachment rate and index exclusion.
/// </summary>
internal interface IEmailAttachmentPicker
{
    /// <summary>
    /// Picks an attachment from the pool, or returns null based on the attachment rate.
    /// Never returns the file at <paramref name="nativeFileIndex"/> (never picks self).
    /// </summary>
    /// <param name="nativeFileIndex">Index of the file currently being generated.</param>
    /// <param name="attachmentRate">Attachment rate as a percentage (0–100).</param>
    /// <param name="pool">Pool of native files eligible to be attached.</param>
    /// <param name="seeded">Seeded random source for deterministic output.</param>
    /// <returns>An <see cref="AttachmentInfo"/> for the chosen file, or null.</returns>
    AttachmentInfo? Pick(long nativeFileIndex, double attachmentRate, IReadOnlyList<NativeFileReference> pool, Random seeded);
}

/// <summary>
/// Default implementation of <see cref="IEmailAttachmentPicker"/>.
/// </summary>
internal sealed class EmailAttachmentPicker : IEmailAttachmentPicker
{
    internal static readonly IEmailAttachmentPicker Default = new EmailAttachmentPicker();

    /// <summary>
    /// Static placeholder pool built from <see cref="PlaceholderFiles"/>.
    /// Items use negative indices so they never collide with real file indices (≥ 0).
    /// </summary>
    internal static readonly IReadOnlyList<NativeFileReference> PlaceholderPool = new NativeFileReference[]
    {
        new(-1L, "attachment.jpg", PlaceholderFiles.GetContent("jpg")),
        new(-2L, "attachment.tiff", PlaceholderFiles.GetContent("tiff")),
        new(-3L, "attachment.pdf", PlaceholderFiles.GetContent("pdf")),
    };

    /// <inheritdoc/>
    public AttachmentInfo? Pick(long nativeFileIndex, double attachmentRate, IReadOnlyList<NativeFileReference> pool, Random seeded)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(seeded);

        var rate = Math.Max(0.0, Math.Min(100.0, attachmentRate));

        if (rate <= 0.0 || pool.Count == 0)
        {
            return null;
        }

        if (seeded.NextDouble() * 100.0 >= rate)
        {
            return null;
        }

        var candidates = new List<NativeFileReference>(pool.Count);
        foreach (var r in pool)
        {
            if (r.Index != nativeFileIndex)
            {
                candidates.Add(r);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var chosen = candidates[seeded.Next(candidates.Count)];
        return new AttachmentInfo
        {
            FileName = chosen.FileName,
            Content = chosen.Content,
            ContentType = chosen.ContentType,
        };
    }
}
