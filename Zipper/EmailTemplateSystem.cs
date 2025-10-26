using System;
using System.Collections.Generic;
using System.Linq;

namespace Zipper
{
    /// <summary>
    /// Provides predefined email templates for generating realistic test data
    /// </summary>
    public static class EmailTemplateSystem
    {
        private static readonly Random _random = new Random();

        /// <summary>
        /// Email template categories for variety
        /// </summary>
        public enum EmailCategory
        {
            Business,
            Personal,
            Technical,
            Marketing,
            Legal,
            Financial,
            Notification,
            Support
        }

        /// <summary>
        /// Gets a random email template from the available categories
        /// </summary>
        /// <param name="recipientIndex">Index used to generate recipient email</param>
        /// <param name="senderIndex">Index used to generate sender email</param>
        /// <param name="category">Optional category constraint</param>
        /// <returns>EmailTemplate with realistic content</returns>
        public static EmailTemplate GetRandomTemplate(int recipientIndex, int senderIndex, EmailCategory? category = null)
        {
            var selectedCategory = category ?? GetRandomCategory();
            var templates = GetTemplatesForCategory(selectedCategory);
            var baseTemplate = templates[_random.Next(templates.Count)];

            return new EmailTemplate
            {
                To = GenerateEmailAddress(recipientIndex, "recipient"),
                From = GenerateEmailAddress(senderIndex, "sender"),
                Subject = GenerateSubject(baseTemplate.Subject, recipientIndex, senderIndex),
                Body = GenerateBody(baseTemplate.Body, recipientIndex, senderIndex, selectedCategory),
                SentDate = GenerateSentDate(selectedCategory),
                Cc = GenerateCcAddresses(selectedCategory, recipientIndex),
                IsHighPriority = ShouldBeHighPriority(selectedCategory),
                RequestReadReceipt = ShouldRequestReadReceipt(selectedCategory),
                ReplyTo = GenerateReplyToAddress(senderIndex, selectedCategory)
            };
        }

        /// <summary>
        /// Gets a template suitable for specific file generation contexts
        /// </summary>
        /// <param name="context">Context information for template generation</param>
        /// <returns>EmailTemplate tailored to the context</returns>
        public static EmailTemplate GetContextualTemplate(EmailContext context)
        {
            var baseTemplate = GetContextualBaseTemplate(context);

            return new EmailTemplate
            {
                To = GenerateEmailAddress(context.RecipientIndex, context.RecipientType ?? "recipient"),
                From = GenerateEmailAddress(context.SenderIndex, context.SenderType ?? "sender"),
                Subject = GenerateSubject(baseTemplate.Subject, context.RecipientIndex, context.SenderIndex),
                Body = GenerateBody(baseTemplate.Body, context.RecipientIndex, context.SenderIndex, context.Category),
                SentDate = context.SentDate ?? GenerateSentDate(context.Category),
                Cc = GenerateCcAddresses(context.Category, context.RecipientIndex),
                IsHighPriority = context.IsHighPriority ?? ShouldBeHighPriority(context.Category),
                RequestReadReceipt = context.RequestReadReceipt ?? ShouldRequestReadReceipt(context.Category),
                ReplyTo = GenerateReplyToAddress(context.SenderIndex, context.Category)
            };
        }

        /// <summary>
        /// Generates email addresses with realistic domains
        /// </summary>
        public static string GenerateEmailAddress(int index, string type)
        {
            var domains = new[]
            {
                "example.com", "test.org", "sample.net", "demo.co", "mock.io",
                "acme.com", "techcorp.net", "business.org", "service.co", "platform.io"
            };

            var domain = domains[index % domains.Length];
            var userType = type.ToLowerInvariant();

            return $"{userType}{index:D3}@{domain}";
        }

        #region Private Methods

        private static EmailCategory GetRandomCategory()
        {
            var categories = Enum.GetValues<EmailCategory>();
            return categories[_random.Next(categories.Length)];
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
                _ => BusinessTemplates
            };
        }

        private static EmailTemplateBase GetContextualBaseTemplate(EmailContext context)
        {
            var templates = GetTemplatesForCategory(context.Category);
            return templates[context.TemplateIndex % templates.Count];
        }

        private static string GenerateSubject(string baseSubject, int recipientIndex, int senderIndex)
        {
            var replacements = new Dictionary<string, string>
            {
                ["{recipient}"] = $"Recipient {recipientIndex:D3}",
                ["{sender}"] = $"Sender {senderIndex:D3}",
                ["{case}"] = $"CASE{recipientIndex:D6}",
                ["{invoice}"] = $"INV{recipientIndex:D6}",
                ["{ticket}"] = $"TKT{recipientIndex:D6}",
                ["{date}"] = DateTime.Now.AddDays(-_random.Next(1, 30)).ToString("MMM dd, yyyy")
            };

            var subject = baseSubject;
            foreach (var replacement in replacements)
            {
                subject = subject.Replace(replacement.Key, replacement.Value);
            }

            return subject;
        }

        private static string GenerateBody(string baseBody, int recipientIndex, int senderIndex, EmailCategory category)
        {
            var replacements = new Dictionary<string, string>
            {
                ["{recipient}"] = $"Recipient {recipientIndex:D3}",
                ["{sender}"] = $"Sender {senderIndex:D3}",
                ["{company}"] = $"Company {senderIndex % 100 + 1}",
                ["{department}"] = GetRandomDepartment(),
                ["{project}"] = $"Project {recipientIndex % 50 + 1}",
                ["{amount}"] = $"${(_random.Next(100, 50000)):N2}",
                ["{deadline}"] = DateTime.Now.AddDays(_random.Next(1, 90)).ToString("MMM dd, yyyy"),
                ["{meeting}"] = DateTime.Now.AddDays(_random.Next(1, 14)).ToString("MMM dd, yyyy 'at' HH:mm")
            };

            var body = baseBody;
            foreach (var replacement in replacements)
            {
                body = body.Replace(replacement.Key, replacement.Value);
            }

            return body;
        }

        private static DateTime GenerateSentDate(EmailCategory category)
        {
            var baseDaysAgo = category switch
            {
                EmailCategory.Notification => _random.Next(1, 7),
                EmailCategory.Personal => _random.Next(1, 30),
                EmailCategory.Business => _random.Next(1, 60),
                EmailCategory.Technical => _random.Next(1, 45),
                EmailCategory.Marketing => _random.Next(1, 90),
                EmailCategory.Legal => _random.Next(1, 180),
                EmailCategory.Financial => _random.Next(1, 45),
                EmailCategory.Support => _random.Next(1, 14),
                _ => _random.Next(1, 30)
            };

            return DateTime.Now.AddDays(-baseDaysAgo).AddHours(_random.Next(-23, 24)).AddMinutes(_random.Next(-59, 60));
        }

        private static string? GenerateCcAddresses(EmailCategory category, int recipientIndex)
        {
            if (_random.NextDouble() > GetCcProbability(category))
                return null;

            var ccCount = _random.Next(1, 4);
            var ccAddresses = new List<string>();

            for (int i = 0; i < ccCount; i++)
            {
                var ccIndex = recipientIndex + i + 1;
                ccAddresses.Add(GenerateEmailAddress(ccIndex, "cc"));
            }

            return string.Join(", ", ccAddresses);
        }

        private static bool ShouldBeHighPriority(EmailCategory category)
        {
            return category switch
            {
                EmailCategory.Legal => _random.NextDouble() > 0.7,
                EmailCategory.Financial => _random.NextDouble() > 0.8,
                EmailCategory.Support => _random.NextDouble() > 0.9,
                EmailCategory.Technical => _random.NextDouble() > 0.85,
                _ => _random.NextDouble() > 0.95
            };
        }

        private static bool ShouldRequestReadReceipt(EmailCategory category)
        {
            return category switch
            {
                EmailCategory.Legal => _random.NextDouble() > 0.3,
                EmailCategory.Financial => _random.NextDouble() > 0.4,
                EmailCategory.Business => _random.NextDouble() > 0.7,
                _ => _random.NextDouble() > 0.9
            };
        }

        private static string? GenerateReplyToAddress(int senderIndex, EmailCategory category)
        {
            if (_random.NextDouble() > GetReplyToProbability(category))
                return null;

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
                _ => 0.2
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
                _ => 0.1
            };
        }

        private static string GetRandomDepartment()
        {
            var departments = new[]
            {
                "Engineering", "Sales", "Marketing", "HR", "Finance",
                "Legal", "Operations", "IT", "Customer Support", "R&D",
                "Product", "Design", "QA", "Security", "Compliance"
            };

            return departments[_random.Next(departments.Length)];
        }

        #endregion

        #region Template Definitions

        private record EmailTemplateBase(string Subject, string Body);

        private static readonly List<EmailTemplateBase> BusinessTemplates = new()
        {
            new("Q{quarter} Business Review - {company}", "Dear {recipient},\n\nI hope this email finds you well. I'm writing to schedule our Q{quarter} business review meeting. We'll be discussing:\n\n• Project {project} status\n• Financial performance\n• Strategic objectives\n• Team goals for next quarter\n\nThe meeting is scheduled for {meeting}. Please confirm your availability.\n\nBest regards,\n{sender}"),
            new("Contract Agreement - {case}", "Hi {recipient},\n\nAttached please find the contract agreement for your review. This outlines the terms we discussed in our recent meeting.\n\nKey highlights:\n• Duration: 12 months\n• Service level: Premium\n• Support: 24/7\n• Pricing: {amount}\n\nPlease review and return with any questions or concerns.\n\nRegards,\n{sender}"),
            new("Meeting Follow-up - {project}", "Dear {recipient},\n\nThank you for attending today's meeting regarding {project}. As discussed, here are the action items:\n\n1. Review requirements by {deadline}\n2. Prepare budget proposal\n3. Schedule technical review\n4. Assign team members\n\nLet me know if you have any questions.\n\nBest,\n{sender}")
        };

        private static readonly List<EmailTemplateBase> TechnicalTemplates = new()
        {
            new("Security Alert - System {case}", "Hi {recipient},\n\nOur security monitoring system detected unusual activity related to your account. Details:\n\n• Timestamp: {date}\n• IP Address: Detected\n• Activity: Multiple failed login attempts\n• Risk Level: Medium\n\nPlease review your account and change your password if necessary.\n\nIT Security Team\n{sender}"),
            new("System Maintenance Notice", "Dear {recipient},\n\nWe will be performing scheduled maintenance on our systems:\n\n• Start: {meeting}\n• Duration: 4 hours\n• Impact: Service temporarily unavailable\n• Systems affected: Main application\n\nPlease plan accordingly and save your work before the maintenance window.\n\nTechnical Operations\n{sender}"),
            new("Bug Report - {ticket}", "Hi {recipient},\n\nWe've identified a bug in the system that needs your attention:\n\n• Bug ID: {ticket}\n• Priority: High\n• Component: Authentication module\n• Description: Users experiencing login issues\n• Steps to reproduce: [Documentation attached]\n\nPlease investigate and provide an ETA for resolution.\n\nThanks,\n{sender}")
        };

        private static readonly List<EmailTemplateBase> SupportTemplates = new()
        {
            new("Support Ticket {ticket} - Response Required", "Dear {recipient},\n\nThank you for contacting our support team. Your ticket {ticket} has been created and is currently being reviewed by our technical specialists.\n\nIssue Summary: {description}\nPriority: {priority}\nExpected Response Time: 2-4 hours\n\nWe'll update you as soon as we have more information.\n\nCustomer Support\n{sender}"),
            new("Your Support Ticket {ticket} Has Been Resolved", "Hi {recipient},\n\nGood news! Your support ticket {ticket} has been resolved.\n\nResolution: {solution}\nWe recommend restarting your application and clearing your cache.\n\nIf the issue persists, please reply to this email and reference ticket {ticket}.\n\nBest regards,\n{sender}")
        };

        // Additional template categories can be added here...
        private static readonly List<EmailTemplateBase> PersonalTemplates = new()
        {
            new("Family Photos", "Hi {recipient},\n\nSharing some family photos from our recent vacation. Hope you enjoy them!\n\nBest wishes,\n{sender}"),
            new("Weekend Plans", "Hi {recipient},\n\nAre you free this weekend? I was thinking we could catch up at {place}.\n\nLet me know!\n{sender}"),
            new("Birthday Invitation", "Dear {recipient},\n\nYou're invited to celebrate my birthday on {date} at {venue}.\n\nLooking forward to seeing you there!\n\nBest,\n{sender}")
        };

        private static readonly List<EmailTemplateBase> MarketingTemplates = new()
        {
            new("Special Offer - 50% Off", "Hi {recipient},\n\nLimited time offer! Get 50% off on all products until {deadline}.\n\nUse code: SAVE50\n\nShop now at {website}\n\nBest regards,\n{sender}"),
            new("Newsletter - {month} Updates", "Dear {recipient},\n\nCheck out our latest updates for {month}:\n\n• New product launches\n• Special promotions\n• Industry insights\n\nRead more on our website.\n\nMarketing Team"),
            new("Product Launch Invitation", "Hi {recipient},\n\nYou're invited to our product launch event!\n\nDate: {date}\nTime: {meeting}\nLocation: {venue}\n\nRSVP by {deadline}\n\n{sender}")
        };

        private static readonly List<EmailTemplateBase> LegalTemplates = new()
        {
            new("Contract Review Required", "Dear {recipient},\n\nPlease find attached contract {case} for your review and signature.\n\nKey terms:\n• Duration: 12 months\n• Value: {amount}\n• Start date: {deadline}\n\nPlease return signed copy by {date}.\n\nRegards,\n{sender}"),
            new("Legal Notice - Case {case}", "To: {recipient}\nFrom: {sender}\nSubject: Legal Notice\n\nThis is formal notification regarding case {case}. Please review attached documents and respond within 5 business days.\n\nLegal Department"),
            new("Confidential Agreement", "CONFIDENTIAL - Privileged & Attorney-Client Communication\n\nDear {recipient},\n\nFollowing up on our discussion regarding {project}. Please find the revised agreement attached.\n\nConfidentiality applies to all contents.\n\n{sender}")
        };

        private static readonly List<EmailTemplateBase> FinancialTemplates = new()
        {
            new("Monthly Statement - {account}", "Dear {recipient},\n\nYour monthly statement for account {account} is now available.\n\n• Balance: {amount}\n• Due date: {deadline}\n• Minimum payment: ${payment}\n\nAccess your full statement online.\n\nFinancial Services"),
            new("Investment Update - {quarter}", "Hi {recipient},\n\nYour {quarter} investment portfolio update:\n\n• Current value: {amount}\n• Growth: +{growth}%\n• Top performer: {company}\n\nReview detailed reports in your portal.\n\n{sender}"),
            new("Transaction Alert", "Security Alert: Transaction detected\n\nAmount: {amount}\nMerchant: {company}\nTime: {meeting}\n\nIf this wasn't you, please contact us immediately.\n\nBank Security Team")
        };

        private static readonly List<EmailTemplateBase> NotificationTemplates = new()
        {
            new("System Maintenance Notice", "Hi {recipient},\n\nScheduled maintenance will occur on {date} from {start_time} to {end_time}.\n\nAffected services:\n• Main application\n• API endpoints\n• Dashboard\n\nWe apologize for any inconvenience.\n\n{sender}"),
            new("Password Reset Request", "Hello {recipient},\n\nA password reset was requested for your account.\n\nIf this was you, click here: {reset_link}\nIf not, please secure your account immediately.\n\nThis link expires in 24 hours.\n\n{sender}"),
            new("Welcome to {service}", "Welcome to {service}, {recipient}!\n\nYour account has been successfully created.\n\nNext steps:\n1. Verify your email\n2. Complete your profile\n3. Explore our features\n\nIf you have questions, reply to this email.\n\nWelcome,\n{sender}")
        };

        #endregion
    }

    /// <summary>
    /// Context information for generating contextual email templates
    /// </summary>
    public record EmailContext
    {
        public int RecipientIndex { get; init; }
        public int SenderIndex { get; init; }
        public EmailTemplateSystem.EmailCategory Category { get; init; }
        public int TemplateIndex { get; init; }
        public DateTime? SentDate { get; init; }
        public bool? IsHighPriority { get; init; }
        public bool? RequestReadReceipt { get; init; }
        public string? RecipientType { get; init; }
        public string? SenderType { get; init; }
    }
}