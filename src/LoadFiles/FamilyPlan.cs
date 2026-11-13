using Zipper.Emails;

namespace Zipper.LoadFiles;

/// <summary>
/// Shared family planning component that determines if a document index has an attachment.
/// consumed by composers/writers in Standard, Production Set, and Loadfile-Only modes.
/// </summary>
internal static class FamilyPlan
{
    // ponytail: IsEml (first declared type) is deliberate here — the simulated family paths are
    // Loadfile-Only-only, and --types is rejected with --loadfile-only, so mixes never reach this.
    public static bool HasAttachment(FileGenerationRequest request, long index)
    {
        if (!request.Metadata.WithFamilies || !request.Output.IsEml || request.LoadFile.AttachmentRate <= 0)
        {
            return false;
        }

        var seedVal = request.Metadata.Seed ?? 42L;
#pragma warning disable S2245
        var random = new Random(unchecked((int)(seedVal + index)));
#pragma warning restore S2245

        EmailFactory.SimulateEmailRandomConsumption(random);

        var rate = Math.Max(0.0, Math.Min(100.0, request.LoadFile.AttachmentRate));
        return random.NextDouble() * 100.0 < rate;
    }

    public static string SanitizeAttachmentFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return filename;
        }
        return Path.GetFileName(filename.Replace('\\', '/'));
    }

    public static string GetChildId(string parentId, int attachmentNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attachmentNumber);
        return $"{parentId}_A{attachmentNumber:D3}";
    }

    /// <summary>
    /// Resolves the archive/relative file path for an email attachment. When duplicate attachment
    /// filenames exist on the same email, includes the attachment number suffix to avoid entry collisions.
    /// </summary>
    public static string GetAttachmentPath(FileWorkItem workItem, string filename, int attachmentNumber, bool isDuplicate = false)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attachmentNumber);

        var sanitized = SanitizeAttachmentFilename(filename);
        if (!isDuplicate)
        {
            return $"{workItem.FolderPrefix}{workItem.Index}_{sanitized}";
        }

        var stem = Path.GetFileNameWithoutExtension(sanitized);
        var ext = Path.GetExtension(sanitized);
        return $"{workItem.FolderPrefix}{workItem.Index}_{stem}_A{attachmentNumber:D3}{ext}";
    }

    /// <summary>
    /// Determines whether the attachment filename at <paramref name="attachmentIndex"/> was already
    /// seen earlier in the list of attachment filenames for the same email.
    /// </summary>
    public static bool IsDuplicateAttachmentName(IReadOnlyList<string> attachmentFilenames, int attachmentIndex)
    {
        ArgumentNullException.ThrowIfNull(attachmentFilenames);
        ArgumentOutOfRangeException.ThrowIfNegative(attachmentIndex);
        if (attachmentIndex >= attachmentFilenames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentIndex));
        }

        var current = SanitizeAttachmentFilename(attachmentFilenames[attachmentIndex]);
        for (int i = 0; i < attachmentIndex; i++)
        {
            if (string.Equals(SanitizeAttachmentFilename(attachmentFilenames[i]), current, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetAttachmentTextPath(FileWorkItem workItem, string filename, int attachmentNumber)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attachmentNumber);

        var stem = Path.GetFileNameWithoutExtension(SanitizeAttachmentFilename(filename));
        var suffix = attachmentNumber > 1 ? $"_A{attachmentNumber:D3}" : string.Empty;
        return $"{workItem.FolderPrefix}{workItem.Index}_{stem}{suffix}.txt";
    }

    public static long GetSimulatedChildCount(FileGenerationRequest request)
    {
        if (!request.Metadata.WithFamilies || !request.Output.IsEml || request.LoadFile.AttachmentRate <= 0)
        {
            return 0;
        }

        if (request.LoadFile.AttachmentRate >= 100)
        {
            return Math.Max(0, request.Output.FileCount);
        }

        long childCount = 0;
        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            if (HasAttachment(request, i))
            {
                childCount++;
            }
        }
        return childCount;
    }

    internal static int GetTemplatesCountForCategory(EmailCategory category) => EmailFactory.GetTemplatesCountForCategory(category);
}
