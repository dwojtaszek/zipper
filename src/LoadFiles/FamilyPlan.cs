using System.IO;
using Zipper.Emails;

namespace Zipper.LoadFiles;

/// <summary>
/// Shared family planning component that determines if a document index has an attachment.
/// consumed by composers/writers in Standard, Production Set, and Loadfile-Only modes.
/// </summary>
internal static class FamilyPlan
{
    public static bool HasAttachment(FileGenerationRequest request, long index)
    {
        if (!request.Metadata.WithFamilies || !request.Output.IsEml || request.LoadFile.AttachmentRate <= 0)
        {
            return false;
        }

        var seedVal = request.Metadata.Seed ?? 42L;
        var random = new Random(unchecked((int)(seedVal + index)));

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

    public static long GetSimulatedChildCount(FileGenerationRequest request)
    {
        if (!request.Metadata.WithFamilies || !request.Output.IsEml || request.LoadFile.AttachmentRate <= 0)
        {
            return 0;
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

    private static int GetTemplatesCountForCategory(EmailCategory category)
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
