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

        SimulateEmailRandomConsumption(random);

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

    private static void SimulateEmailRandomConsumption(Random random)
    {
        // 1. GetRandomCategory
        var categories = Enum.GetValues<EmailCategory>();
        var selectedCategory = categories[random.Next(categories.Length)];

        // 2. template selection
        var templateCount = GetTemplatesCountForCategory(selectedCategory);
        _ = random.Next(templateCount);

        // 3 & 4. BuildReplacements (called twice: once for Subject, once for Body)
        // Each call to BuildReplacements calls random exactly 27 times.
        for (int i = 0; i < 54; i++)
        {
            _ = random.Next();
        }

        // 5. GenerateSentDate
        _ = random.Next(); // baseDaysAgo
        _ = random.Next(); // AddHours
        _ = random.Next(); // AddMinutes

        // 6. GenerateCcAddresses
        var ccProb = GetCcProbability(selectedCategory);
        if (random.NextDouble() <= ccProb)
        {
            _ = random.Next(1, 4);
        }

        // 7. ShouldBeHighPriority
        _ = random.NextDouble();

        // 8. ShouldRequestReadReceipt
        _ = random.NextDouble();

        // 9. GenerateReplyToAddress
        _ = random.NextDouble() <= GetReplyToProbability(selectedCategory);
    }

    internal static int GetTemplatesCountForCategory(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.Business => 8,
            EmailCategory.Personal => 3,
            EmailCategory.Technical => 3,
            EmailCategory.Marketing => 3,
            EmailCategory.Legal => 3,
            EmailCategory.Financial => 3,
            EmailCategory.Notification => 3,
            EmailCategory.Support => 2,
            EmailCategory.Healthcare => 5,
            EmailCategory.Education => 5,
            EmailCategory.Ecommerce => 5,
            EmailCategory.Travel => 5,
            _ => 8,
        };
    }

    private static double GetCcProbability(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.Business => 0.6,
            EmailCategory.Technical => 0.4,
            EmailCategory.Legal => 0.5,
            EmailCategory.Financial => 0.4,
            EmailCategory.Support => 0.3,
            EmailCategory.Marketing => 0.8,
            EmailCategory.Healthcare => 0.3,
            EmailCategory.Education => 0.5,
            EmailCategory.Ecommerce => 0.2,
            EmailCategory.Travel => 0.4,
            _ => 0.2,
        };
    }

    private static double GetReplyToProbability(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.Support => 0.7,
            EmailCategory.Marketing => 0.8,
            EmailCategory.Business => 0.3,
            EmailCategory.Technical => 0.4,
            EmailCategory.Healthcare => 0.2,
            EmailCategory.Education => 0.4,
            EmailCategory.Ecommerce => 0.6,
            EmailCategory.Travel => 0.3,
            _ => 0.1,
        };
    }
}
