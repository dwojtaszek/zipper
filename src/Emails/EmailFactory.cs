using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Zipper.Emails;

internal static class EmailFactory
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly EmailData Data = LoadEmailData();

    private static readonly string[] EmailDomains = Data.EmailDomains;
    private static readonly string[] Departments = Data.Departments;
    private static readonly string[] Places = Data.Places;
    private static readonly string[] Venues = Data.Venues;
    private static readonly string[] Websites = Data.Websites;
    private static readonly string[] Services = Data.Services;

    private static readonly Dictionary<EmailCategory, List<EmailTemplateBase>> TemplatesByCategory =
        Data.Templates.ToDictionary(
            kvp => (EmailCategory)Enum.Parse(typeof(EmailCategory), kvp.Key, true),
            kvp => kvp.Value);

    // Aliases into Data.Templates; treat as read-only — mutating would corrupt the shared data.
    private static readonly List<EmailTemplateBase> BusinessTemplates = TemplatesByCategory[EmailCategory.Business];
    private static readonly List<EmailTemplateBase> TechnicalTemplates = TemplatesByCategory[EmailCategory.Technical];
    private static readonly List<EmailTemplateBase> SupportTemplates = TemplatesByCategory[EmailCategory.Support];
    private static readonly List<EmailTemplateBase> PersonalTemplates = TemplatesByCategory[EmailCategory.Personal];
    private static readonly List<EmailTemplateBase> MarketingTemplates = TemplatesByCategory[EmailCategory.Marketing];
    private static readonly List<EmailTemplateBase> LegalTemplates = TemplatesByCategory[EmailCategory.Legal];
    private static readonly List<EmailTemplateBase> FinancialTemplates = TemplatesByCategory[EmailCategory.Financial];
    private static readonly List<EmailTemplateBase> NotificationTemplates = TemplatesByCategory[EmailCategory.Notification];
    private static readonly List<EmailTemplateBase> HealthcareTemplates = TemplatesByCategory[EmailCategory.Healthcare];
    private static readonly List<EmailTemplateBase> EducationTemplates = TemplatesByCategory[EmailCategory.Education];
    private static readonly List<EmailTemplateBase> EcommerceTemplates = TemplatesByCategory[EmailCategory.Ecommerce];
    private static readonly List<EmailTemplateBase> TravelTemplates = TemplatesByCategory[EmailCategory.Travel];

    internal static Email Create(FileWorkItem item, FileGenerationRequest request, Random seeded)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(seeded);
        var referenceDate = request.Metadata.Seed.HasValue
            ? new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc)
            : DateTime.UtcNow;
        return Create((int)item.Index, (int)item.Index, category: null, seeded, referenceDate);
    }

    internal static Email Create(int recipientIndex, int senderIndex, EmailCategory? category, Random random)
    {
        return Create(recipientIndex, senderIndex, category, random, DateTime.UtcNow);
    }

    internal static Email Create(int recipientIndex, int senderIndex, EmailCategory? category, Random random, DateTime referenceDate)
    {
        ArgumentNullException.ThrowIfNull(random);
        var selectedCategory = category ?? GetRandomCategory(random);
        var templates = GetTemplatesForCategory(selectedCategory);
        var baseTemplate = templates[random.Next(templates.Count)];
        return new Email
        {
            To = GenerateEmailAddress(recipientIndex, "recipient"),
            From = GenerateEmailAddress(senderIndex, "sender"),
            Subject = GenerateSubject(baseTemplate.Subject, recipientIndex, senderIndex, random, referenceDate),
            Body = GenerateBody(baseTemplate.Body, recipientIndex, senderIndex, random, referenceDate),
            SentDate = GenerateSentDate(selectedCategory, random, referenceDate),
            Cc = GenerateCcAddresses(selectedCategory, recipientIndex, random),
            IsHighPriority = ShouldBeHighPriority(selectedCategory, random),
            RequestReadReceipt = ShouldRequestReadReceipt(selectedCategory, random),
            ReplyTo = GenerateReplyToAddress(senderIndex, selectedCategory, random),
        };
    }

    internal static Email CreateContextual(EmailContext context, Random random)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(random);
        var referenceDate = DateTime.UtcNow;
        var baseTemplate = GetContextualBaseTemplate(context);
        return new Email
        {
            To = GenerateEmailAddress(context.RecipientIndex, context.RecipientType ?? "recipient"),
            From = GenerateEmailAddress(context.SenderIndex, context.SenderType ?? "sender"),
            Subject = GenerateSubject(baseTemplate.Subject, context.RecipientIndex, context.SenderIndex, random, referenceDate),
            Body = GenerateBody(baseTemplate.Body, context.RecipientIndex, context.SenderIndex, random, referenceDate),
            SentDate = context.SentDate ?? GenerateSentDate(context.Category, random, referenceDate),
            Cc = GenerateCcAddresses(context.Category, context.RecipientIndex, random),
            IsHighPriority = context.IsHighPriority ?? ShouldBeHighPriority(context.Category, random),
            RequestReadReceipt = context.RequestReadReceipt ?? ShouldRequestReadReceipt(context.Category, random),
            ReplyTo = GenerateReplyToAddress(context.SenderIndex, context.Category, random),
        };
    }

    internal static string GenerateEmailAddress(int index, string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var domain = EmailDomains[index % EmailDomains.Length];
        return $"{type.ToLowerInvariant()}{index:D3}@{domain}";
    }

    private static EmailCategory GetRandomCategory(Random random)
    {
        var categories = Enum.GetValues<EmailCategory>();
        return categories[random.Next(categories.Length)];
    }

    private static List<EmailTemplateBase> GetTemplatesForCategory(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.Business => BusinessTemplates,
            EmailCategory.Personal => PersonalTemplates,
            EmailCategory.Technical => TechnicalTemplates,
            EmailCategory.Marketing => MarketingTemplates,
            EmailCategory.Legal => LegalTemplates,
            EmailCategory.Financial => FinancialTemplates,
            EmailCategory.Notification => NotificationTemplates,
            EmailCategory.Support => SupportTemplates,
            EmailCategory.Healthcare => HealthcareTemplates,
            EmailCategory.Education => EducationTemplates,
            EmailCategory.Ecommerce => EcommerceTemplates,
            EmailCategory.Travel => TravelTemplates,
            _ => BusinessTemplates,
        };
    }

    private static EmailTemplateBase GetContextualBaseTemplate(EmailContext context)
    {
        var templates = GetTemplatesForCategory(context.Category);
        return templates[context.TemplateIndex % templates.Count];
    }

    private static Dictionary<string, string> BuildReplacements(int recipientIndex, int senderIndex, Random random, DateTime referenceDate)
    {
        return new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["{recipient}"] = $"Recipient {recipientIndex:D3}",
            ["{sender}"] = $"Sender {senderIndex:D3}",
            ["{case}"] = $"CASE{recipientIndex:D6}",
            ["{invoice}"] = $"INV{recipientIndex:D6}",
            ["{ticket}"] = $"TKT{recipientIndex:D6}",
            ["{date}"] = referenceDate.AddDays(-random.Next(1, 30)).ToString("MMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ["{course}"] = $"Course {(recipientIndex % 100) + 1}",
            ["{project}"] = $"Project {(recipientIndex % 50) + 1}",
            ["{quarter}"] = $"Q{random.Next(1, 5)}",
            ["{company}"] = $"Company {(senderIndex % 100) + 1}",
            ["{department}"] = GetRandomDepartment(random),
            ["{amount}"] = $"${random.Next(100, 50000):N2}",
            ["{deadline}"] = referenceDate.AddDays(random.Next(1, 90)).ToString("MMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ["{meeting}"] = referenceDate.AddDays(random.Next(1, 14)).ToString("MMM dd, yyyy 'at' HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["{place}"] = GetRandomPlace(random),
            ["{venue}"] = GetRandomVenue(random),
            ["{website}"] = GetRandomWebsite(random),
            ["{service}"] = GetRandomService(random),
            ["{reset_link}"] = $"https://example.com/reset?token={random.Next():x8}{random.Next():x8}",
            ["{growth}"] = $"{random.Next(5, 25)}",
            ["{payment}"] = $"{random.Next(25, 500):N2}",
            ["{account}"] = $"ACC{random.Next(100000, 999999):D6}",
            ["{start_time}"] = $"{random.Next(0, 12):D2}:00 {(random.Next(0, 2) == 0 ? "AM" : "PM")}",
            ["{end_time}"] = $"{random.Next(13, 23):D2}:00 {(random.Next(0, 2) == 0 ? "PM" : "AM")}",
            ["{month}"] = referenceDate.AddMonths(-random.Next(0, 12)).ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture),
            ["{gpa}"] = $"3.{random.Next(4, 9)}",
            ["{courses_completed}"] = $"{random.Next(3, 6)}",
            ["{attendance}"] = $"{random.Next(85, 100)}",
            ["{credits}"] = $"{random.Next(9, 18)}",
            ["{gate}"] = $"A{(senderIndex % 20) + 1}",
            ["{seat}"] = $"{random.Next(1, 30)}{(char)('A' + random.Next(0, 6))}",
            ["{rental_period}"] = $"{random.Next(1, 14)}",
            ["{description}"] = "User account lockout after multiple attempts.",
            ["{solution}"] = "Reset password and cleared lockout flag.",
            ["{priority}"] = (recipientIndex % 3) == 0 ? "High" : "Normal",
        };
    }

    private static string ApplyReplacements(string template, Dictionary<string, string> replacements)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        int openBraceIndex = template.IndexOf('{');
        if (openBraceIndex == -1)
        {
            return template;
        }

        var lookup = replacements.GetAlternateLookup<ReadOnlySpan<char>>();
        StringBuilder? sb = null;
        int currentIndex = 0;

        while (openBraceIndex != -1)
        {
            int closeBraceIndex = template.IndexOf('}', openBraceIndex + 1);
            if (closeBraceIndex == -1)
            {
                break;
            }

            int length = closeBraceIndex - openBraceIndex + 1;
            var keySpan = template.AsSpan(openBraceIndex, length);

            if (lookup.TryGetValue(keySpan, out var value))
            {
                if (sb is null)
                {
                    sb = new StringBuilder(template.Length * 2);
                    sb.Append(template, 0, openBraceIndex);
                }
                else
                {
                    sb.Append(template, currentIndex, openBraceIndex - currentIndex);
                }
                sb.Append(value);
                currentIndex = closeBraceIndex + 1;
                openBraceIndex = template.IndexOf('{', currentIndex);
            }
            else
            {
                openBraceIndex = template.IndexOf('{', openBraceIndex + 1);
            }
        }

        if (sb is null)
        {
            return template;
        }

        if (currentIndex < template.Length)
        {
            sb.Append(template, currentIndex, template.Length - currentIndex);
        }

        return sb.ToString();
    }

    private static string GenerateSubject(string baseSubject, int recipientIndex, int senderIndex, Random random, DateTime referenceDate)
    {
        return ApplyReplacements(baseSubject, BuildReplacements(recipientIndex, senderIndex, random, referenceDate));
    }

    private static string GenerateBody(string baseBody, int recipientIndex, int senderIndex, Random random, DateTime referenceDate)
    {
        return ApplyReplacements(baseBody, BuildReplacements(recipientIndex, senderIndex, random, referenceDate));
    }

    private static DateTime GenerateSentDate(EmailCategory category, Random random, DateTime referenceDate)
    {
        var baseDaysAgo = category switch
        {
            EmailCategory.Notification => random.Next(1, 7),
            EmailCategory.Personal => random.Next(1, 30),
            EmailCategory.Business => random.Next(1, 60),
            EmailCategory.Technical => random.Next(1, 45),
            EmailCategory.Marketing => random.Next(1, 90),
            EmailCategory.Legal => random.Next(1, 180),
            EmailCategory.Financial => random.Next(1, 45),
            EmailCategory.Support => random.Next(1, 14),
            EmailCategory.Healthcare => random.Next(1, 90),
            EmailCategory.Education => random.Next(1, 120),
            EmailCategory.Ecommerce => random.Next(1, 60),
            EmailCategory.Travel => random.Next(1, 365),
            _ => random.Next(1, 30),
        };

        return referenceDate.AddDays(-baseDaysAgo).AddHours(random.Next(-23, 24)).AddMinutes(random.Next(-59, 60));
    }

    private static string? GenerateCcAddresses(EmailCategory category, int recipientIndex, Random random)
    {
        if (random.NextDouble() > GetCcProbability(category))
        {
            return null;
        }

        var ccCount = random.Next(1, 4);
        var ccAddresses = new List<string>();
        for (int i = 0; i < ccCount; i++)
        {
            ccAddresses.Add(GenerateEmailAddress(recipientIndex + i + 1, "cc"));
        }

        return string.Join(", ", ccAddresses);
    }

    private static bool ShouldBeHighPriority(EmailCategory category, Random random)
    {
        return category switch
        {
            EmailCategory.Legal => random.NextDouble() > 0.7,
            EmailCategory.Financial => random.NextDouble() > 0.8,
            EmailCategory.Support => random.NextDouble() > 0.9,
            EmailCategory.Technical => random.NextDouble() > 0.85,
            _ => random.NextDouble() > 0.95,
        };
    }

    private static bool ShouldRequestReadReceipt(EmailCategory category, Random random)
    {
        return category switch
        {
            EmailCategory.Legal => random.NextDouble() > 0.3,
            EmailCategory.Financial => random.NextDouble() > 0.4,
            EmailCategory.Business => random.NextDouble() > 0.7,
            _ => random.NextDouble() > 0.9,
        };
    }

    private static string? GenerateReplyToAddress(int senderIndex, EmailCategory category, Random random)
    {
        if (random.NextDouble() > GetReplyToProbability(category))
        {
            return null;
        }

        return GenerateEmailAddress(senderIndex, "reply");
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

    private static string GetRandomDepartment(Random random)
    {
        return Departments[random.Next(Departments.Length)];
    }

    private static string GetRandomPlace(Random random)
    {
        return Places[random.Next(Places.Length)];
    }

    private static string GetRandomVenue(Random random)
    {
        return Venues[random.Next(Venues.Length)];
    }

    private static string GetRandomWebsite(Random random)
    {
        return Websites[random.Next(Websites.Length)];
    }

    private static string GetRandomService(Random random)
    {
        return Services[random.Next(Services.Length)];
    }

    internal sealed record EmailTemplateBase(string Subject, string Body);

    internal sealed record EmailData(
        string[] EmailDomains,
        string[] Departments,
        string[] Places,
        string[] Venues,
        string[] Websites,
        string[] Services,
        Dictionary<string, List<EmailTemplateBase>> Templates);

    private static EmailData LoadEmailData()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Zipper.Emails.email-templates.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<EmailData>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Failed to deserialize '{resourceName}'.");
    }
}
