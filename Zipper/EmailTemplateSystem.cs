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
            Support,
            Healthcare,
            Education,
            Ecommerce,
            Travel
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
                EmailCategory.Healthcare => HealthcareTemplates,
                EmailCategory.Education => EducationTemplates,
                EmailCategory.Ecommerce => EcommerceTemplates,
                EmailCategory.Travel => TravelTemplates,
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
                ["{date}"] = DateTime.Now.AddDays(-_random.Next(1, 30)).ToString("MMM dd, yyyy"),
                ["{course}"] = $"Course {recipientIndex % 100 + 1}",
                ["{project}"] = $"Project {recipientIndex % 50 + 1}",
                ["{quarter}"] = $"Q{(_random.Next(1, 5))}"
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
                ["{meeting}"] = DateTime.Now.AddDays(_random.Next(1, 14)).ToString("MMM dd, yyyy 'at' HH:mm"),
                ["{date}"] = DateTime.Now.AddDays(-_random.Next(1, 30)).ToString("MMM dd, yyyy"),
                ["{place}"] = GetRandomPlace(),
                ["{venue}"] = GetRandomVenue(),
                ["{website}"] = GetRandomWebsite(),
                ["{service}"] = GetRandomService(),
                ["{reset_link}"] = $"https://example.com/reset?token={Guid.NewGuid():N}",
                ["{quarter}"] = $"Q{(_random.Next(1, 5))}",
                ["{growth}"] = $"{_random.Next(5, 25)}",
                ["{payment}"] = $"{_random.Next(25, 500):N2}",
                ["{account}"] = $"ACC{(_random.Next(100000, 999999)):D6}",
                ["{start_time}"] = $"{_random.Next(0, 12):D2}:00 {_random.Next(0, 2) == 0 ? "AM" : "PM"}",
                ["{end_time}"] = $"{_random.Next(13, 23):D2}:00 {_random.Next(0, 2) == 0 ? "PM" : "AM"}",
                ["{month}"] = DateTime.Now.AddMonths(-_random.Next(0, 12)).ToString("MMMM")
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
                EmailCategory.Healthcare => _random.Next(1, 90),
                EmailCategory.Education => _random.Next(1, 120),
                EmailCategory.Ecommerce => _random.Next(1, 60),
                EmailCategory.Travel => _random.Next(1, 365),
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
                EmailCategory.Healthcare => 0.3,
                EmailCategory.Education => 0.5,
                EmailCategory.Ecommerce => 0.2,
                EmailCategory.Travel => 0.4,
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
                EmailCategory.Healthcare => 0.2,
                EmailCategory.Education => 0.4,
                EmailCategory.Ecommerce => 0.6,
                EmailCategory.Travel => 0.3,
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

        private static string GetRandomPlace()
        {
            var places = new[]
            {
                "Central Park Coffee Shop", "Downtown Conference Center", "Riverside Restaurant",
                "Tech Hub Meeting Room", "City Library Study Area", "Airport Business Lounge",
                "University Campus Hall", "Community Center", "Hotel Conference Room", "Beach Resort"
            };

            return places[_random.Next(places.Length)];
        }

        private static string GetRandomVenue()
        {
            var venues = new[]
            {
                "Grand Ballroom Hotel", "Convention Center Hall A", "Tech Campus Auditorium",
                "City Conference Center", "Business Plaza Meeting Room", "University Lecture Hall",
                "Community Event Space", "Downtown Theater", "Resort Conference Room", "Stadium Suite"
            };

            return venues[_random.Next(venues.Length)];
        }

        private static string GetRandomWebsite()
        {
            var websites = new[]
            {
                "www.techcorp.com", "www.businesshub.org", "www.serviceplatform.net",
                "www.example-site.com", "www.demoservice.io", "www.testplatform.co",
                "www.company-portal.com", "www.business-solutions.org", "www.services.net", "www.platform.io"
            };

            return websites[_random.Next(websites.Length)];
        }

        private static string GetRandomService()
        {
            var services = new[]
            {
                "CloudStorage Pro", "EmailPlatform Plus", "BusinessSuite Enterprise",
                "DataAnalytics Cloud", "SecurityShield Advanced", "CollaborationHub Premium",
                "ProjectManagement Pro", "CustomerPortal Enterprise", "DocumentCloud Plus", "WorkflowOptimizer"
            };

            return services[_random.Next(services.Length)];
        }

        #endregion

        #region Template Definitions

        private record EmailTemplateBase(string Subject, string Body);

        private static readonly List<EmailTemplateBase> BusinessTemplates = new()
        {
            new("Q{quarter} Business Review - {company}", "Dear {recipient},\n\nI hope this email finds you well. I'm writing to schedule our Q{quarter} business review meeting. We'll be discussing:\n\n• Project {project} status\n• Financial performance\n• Strategic objectives\n• Team goals for next quarter\n\nThe meeting is scheduled for {meeting}. Please confirm your availability.\n\nBest regards,\n{sender}"),
            new("Contract Agreement - {case}", "Hi {recipient},\n\nAttached please find the contract agreement for your review. This outlines the terms we discussed in our recent meeting.\n\nKey highlights:\n• Duration: 12 months\n• Service level: Premium\n• Support: 24/7\n• Pricing: {amount}\n\nPlease review and return with any questions or concerns.\n\nRegards,\n{sender}"),
            new("Meeting Follow-up - {project}", "Dear {recipient},\n\nThank you for attending today's meeting regarding {project}. As discussed, here are the action items:\n\n1. Review requirements by {deadline}\n2. Prepare budget proposal\n3. Schedule technical review\n4. Assign team members\n\nLet me know if you have any questions.\n\nBest,\n{sender}"),
            new("Partnership Proposal - {company}", "Dear {recipient},\n\nI hope this email finds you well. Following our recent conversation at {place}, I wanted to formally propose a strategic partnership between our organizations.\n\nProposed collaboration areas:\n• Joint product development\n• Market expansion initiatives\n• Resource sharing opportunities\n• Technology integration\n\nI believe this partnership could generate significant value for both parties. Would you be available for a follow-up discussion next week?\n\nLooking forward to your response.\n\nBest regards,\n{sender}"),
            new("Budget Approval Request - {project}", "Hi {recipient},\n\nI'm writing to request budget approval for the {project} initiative. Based on our detailed analysis, we require funding of {amount} to complete this project successfully.\n\nBudget breakdown:\n• Development resources: 40%\n• Infrastructure and tools: 25%\n• Marketing and promotion: 20%\n• Contingency fund: 15%\n\nExpected ROI: {growth}% within 12 months\nTimeline: {deadline}\n\nPlease review the attached proposal and let me know if you need any additional information.\n\nRegards,\n{sender}"),
            new("Team Performance Review - {department}", "Dear {recipient},\n\nI'm pleased to share the Q{quarter} performance review for the {department} team. Our team has demonstrated exceptional results across all key metrics.\n\nKey achievements:\n• Project completion rate: 95%\n• Client satisfaction score: 4.8/5.0\n• Budget adherence: 98%\n• Team retention: 92%\n\nAreas for improvement:\n• Cross-team collaboration initiatives\n• Advanced skills development\n• Process automation opportunities\n\nLet's schedule a meeting to discuss these results and plan for the next quarter.\n\nBest regards,\n{sender}"),
            new("Client Proposal - {project}", "Dear {recipient},\n\nThank you for the opportunity to present our proposal for {project}. Based on our understanding of your requirements, we've developed a comprehensive solution tailored to your needs.\n\nOur solution includes:\n• Custom software development\n• Cloud infrastructure setup\n• Ongoing support and maintenance\n• User training and documentation\n\nTotal investment: {amount}\nProject timeline: 6 months\nNext steps: Schedule technical deep-dive session\n\nPlease review the attached proposal and let me know your thoughts.\n\nBest regards,\n{sender}"),
            new("Quarterly Business Report", "Hi {recipient},\n\nPlease find attached our Q{quarter} business report highlighting key achievements and strategic initiatives.\n\nExecutive Summary:\n• Revenue growth: {growth}% YoY\n• New client acquisition: 47\n• Product launches: 3\n• Market expansion: 2 new regions\n\nStrategic Focus Areas:\n• Digital transformation acceleration\n• Customer experience enhancement\n• Operational efficiency improvements\n• Talent development programs\n\nFull report attached for your review.\n\nRegards,\n{sender}")
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

        // New category templates for enhanced variety
        private static readonly List<EmailTemplateBase> HealthcareTemplates = new()
        {
            new("Medical Appointment Confirmation - Dr. {sender}", "Dear {recipient},\n\nThis email confirms your upcoming medical appointment:\n\nDate: {date}\nTime: {meeting}\nLocation: {venue}\nDoctor: Dr. {sender}\nDepartment: {department}\n\nPlease arrive 15 minutes early and bring:\n• Insurance card\n• Photo ID\n• List of current medications\n\nIf you need to reschedule, please call us at least 24 hours in advance.\n\nBest regards,\nMedical Center Administration"),
            new("Lab Results Available", "Hello {recipient},\n\nYour recent lab test results are now available through our patient portal at {website}.\n\nTest Details:\n• Test Date: {date}\n• Ordered by: Dr. {sender}\n• Results: Available for review\n\nPlease log in to your secure patient account to review your results. If you have any questions or concerns, please schedule a follow-up appointment.\n\nYour Health Matters,\n{company} Medical Team"),
            new("Prescription Refill Reminder", "Hi {recipient},\n\nThis is a friendly reminder that your prescription is due for refill.\n\nPrescription Details:\n• Medication: [Automated Medication Name]\n• Refill Date: {date}\n• Pharmacy: {place}\n• Prescribing Doctor: Dr. {sender}\n\nPlease contact your pharmacy to arrange for pickup or delivery. If you need a consultation before refill, please schedule an appointment.\n\nStay Healthy,\n{company} Healthcare"),
            new("Annual Check-up Reminder", "Dear {recipient},\n\nIt's time for your annual health check-up! Regular preventive care is essential for maintaining good health.\n\nRecommended Annual Screening:\n• Physical examination\n• Blood work and lab tests\n• Vaccination review\n• Health risk assessment\n\nSchedule your appointment today by visiting {website} or calling our office.\n\nYour Health, Our Priority,\n{company} Medical Center"),
            new("Telehealth Consultation Scheduled", "Hello {recipient},\n\nYour telehealth consultation has been scheduled successfully.\n\nAppointment Details:\n• Date: {date}\n• Time: {meeting}\n• Consultation Type: Video Call\n• Healthcare Provider: Dr. {sender}\n• Meeting Link: [Secure Video Link]\n\nPlease test your device 15 minutes before the appointment. You'll receive a reminder email 1 hour before the consultation.\n\n{company} Telehealth Services")
        };

        private static readonly List<EmailTemplateBase> EducationTemplates = new()
        {
            new("Course Enrollment Confirmation - {course}", "Dear {recipient},\n\nCongratulations! You have been successfully enrolled in {course}.\n\nCourse Details:\n• Course Code: {case}\n• Start Date: {date}\n• Instructor: {sender}\n• Format: Online\n• Duration: 12 weeks\n\nNext Steps:\n1. Access your student portal at {website}\n2. Review course materials\n3. Join orientation session on {meeting}\n4. Complete pre-course assessment\n\nWe're excited to have you join our learning community!\n\nBest regards,\n{company} Education Team"),
            new("Assignment Due Reminder - {project}", "Hi {recipient},\n\nThis is a friendly reminder that your assignment for {project} is due soon.\n\nAssignment Details:\n• Course: {course}\n• Assignment Title: {project}\n• Due Date: {deadline}\n• Submission Portal: {website}\n• Maximum Points: 100\n\nLate submissions will incur a 10% penalty per day. Please ensure you submit your work before the deadline.\n\nGood luck with your assignment!\n\n{sender} - Course Instructor"),
            new("Student Progress Report", "Dear {recipient},\n\nPlease find your student progress report for the {quarter} academic period.\n\nAcademic Performance:\n• Overall GPA: 3.{_random.Next(4, 9)}\n• Courses Completed: {_random.Next(3, 6)}\n• Attendance Rate: {_random.Next(85, 100)}%\n• Credits Earned: {_random.Next(9, 18)}\n\nAreas of Excellence:\n• Class Participation\n• Assignment Quality\n• Collaboration Skills\n\nFor detailed grades and feedback, please visit your student portal.\n\nRegards,\n{company} Academic Advisors"),
            new("Online Class Invitation", "Hello {recipient},\n\nYou're invited to join our online class session tomorrow.\n\nClass Information:\n• Subject: {course}\n• Topic: {project}\n• Time: {meeting}\n• Platform: {service}\n• Meeting Link: {website}/join/{case}\n\nPlease ensure you have:\n• Stable internet connection\n• Webcam and microphone\n• Course materials downloaded\n\nWe look forward to your active participation!\n\nBest regards,\n{sender} - Education Coordinator"),
            new("Academic Achievement Award", "Congratulations {recipient}!\n\nWe are pleased to inform you that you have been selected for the Academic Excellence Award for {quarter}.\n\nAward Details:\n• Achievement: Outstanding Academic Performance\n• Recognition: Certificate and Scholarship\n• Ceremony Date: {date}\n• Venue: {venue}\n\nYour dedication to academic excellence has not gone unnoticed. This award recognizes your hard work and commitment to learning.\n\nWe hope to see you and your family at the awards ceremony.\n\nWith Pride,\n{company} Academic Committee")
        };

        private static readonly List<EmailTemplateBase> EcommerceTemplates = new()
        {
            new("Order Confirmation #{case}", "Dear {recipient},\n\nThank you for your order! We're pleased to confirm that your order has been received and is being processed.\n\nOrder Details:\n• Order Number: {case}\n• Order Date: {date}\n• Total Amount: {amount}\n• Shipping Method: Standard Delivery\n• Expected Delivery: {deadline}\n\nTrack your order at: {website}/track/{case}\n\nYou'll receive another email when your order ships.\n\nHappy Shopping!\n{company} Customer Service"),
            new("Your Order Has Shipped!", "Great news, {recipient}!\n\nYour order #{case} has been shipped and is on its way to you.\n\nShipping Information:\n• Carrier: Express Delivery\n• Tracking Number: TRK{case}\n• Estimated Delivery: {deadline}\n• Shipping Address: [Your Address]\n\nTrack your package: {website}/track/{case}\n\nThank you for shopping with {company}!\n\nBest regards,\nCustomer Care Team"),
            new("Special Offer - 30% Off Everything!", "Hi {recipient},\n\nExclusive offer just for you! Get 30% off on all products for the next 48 hours.\n\nOffer Details:\n• Discount: 30% OFF\n• Valid Until: {deadline}\n• Promo Code: SPECIAL30\n• Website: {website}\n\nFeatured Categories:\n• Electronics and Gadgets\n• Fashion and Accessories\n• Home and Garden\n• Sports and Outdoors\n\nDon't miss out on these amazing deals!\n\nShop Now: {website}\n\nHappy Shopping,\n{company} Team"),
            new("Cart Abandonment Reminder", "Hello {recipient},\n\nWe noticed you left some items in your shopping cart. Don't miss out!\n\nItems in Your Cart:\n• {project} - {amount}\n• Additional items waiting for you\n\nComplete your purchase now and enjoy:\n• Free shipping on orders over $50\n• Easy returns within 30 days\n• Secure payment processing\n\nReturn to Cart: {website}/cart/{case}\n\nQuestions? We're here to help at support@{company}.com\n\nBest regards,\n{company} Shopping Team"),
            new("Product Review Request", "Hi {recipient},\n\nHow do you like your recent purchase from {company}? We'd love to hear your feedback!\n\nProduct Purchased: {project}\nOrder Number: {case}\nPurchase Date: {date}\n\nShare your experience and help other shoppers make informed decisions. Your review helps us improve our products and services.\n\nLeave a Review: {website}/review/{case}\n\nAs a thank you, you'll receive 10% off your next purchase.\n\nThank you for being a valued customer!\n\n{company} Team")
        };

        private static readonly List<EmailTemplateBase> TravelTemplates = new()
        {
            new("Booking Confirmation - {project}", "Dear {recipient},\n\nYour travel booking has been confirmed! Get ready for an amazing experience.\n\nBooking Details:\n• Confirmation Number: {case}\n• Destination: {project}\n• Travel Dates: {date} - {deadline}\n• Total Cost: {amount}\n• Booking Status: Confirmed\n\nWhat's Included:\n• Round-trip airfare\n• Hotel accommodation\n• Daily breakfast\n• Airport transfers\n\nDownload your itinerary: {website}/itinerary/{case}\n\nHave a wonderful trip!\n\n{company} Travel Team"),
            new("Flight Check-in Reminder", "Hello {recipient},\n\nYour flight check-in is now open! Secure your preferred seat and save time at the airport.\n\nFlight Information:\n• Flight Number: {case}\n• Departure: {date} at {meeting}\n• Gate: A{sender % 20 + 1}\n• Seat: {_random.Next(1, 30)}{Random.Shared.Next('A', 'F')}\n• Destination: {project}\n\nCheck-in Now: {website}/checkin/{case}\n\nPlease arrive at the airport at least 2 hours before departure.\n\nSafe Travels,\n{company} Airlines"),
            new("Hotel Reservation Confirmation", "Dear {recipient},\n\nWelcome to {venue}! Your hotel reservation has been confirmed.\n\nReservation Details:\n• Confirmation Number: {case}\n• Check-in: {date}\n• Check-out: {deadline}\n• Room Type: Deluxe Suite\n• Guests: 2 Adults\n• Rate: {amount}/night\n\nHotel Amenities:\n• Free Wi-Fi\n• Swimming Pool\n• Fitness Center\n• Complimentary Breakfast\n• Spa Services\n\nWe look forward to making your stay memorable!\n\n{company} Hospitality Team"),
            new("Travel Insurance Certificate", "Hi {recipient},\n\nYour travel insurance policy has been issued successfully. You're now covered for your upcoming trip!\n\nPolicy Details:\n• Policy Number: INS{case}\n• Coverage Period: {date} - {deadline}\n• Destination: {project}\n• Coverage Amount: {amount}\n• Premium Paid: {payment}\n\nCoverage Includes:\n• Medical emergencies\n• Trip cancellation\n• Lost baggage\n• Flight delays\n• Emergency evacuation\n\nAccess your policy documents: {website}/policy/{case}\n\nTravel Safe,\n{company} Insurance Services"),
            new("Car Rental Confirmation", "Hello {recipient},\n\nYour car rental has been confirmed for your upcoming trip to {project}.\n\nRental Details:\n• Confirmation Number: {case}\n• Pickup Date: {date}\n• Pickup Location: {place}\n• Vehicle: Compact Car\n• Daily Rate: {amount}\n• Rental Period: {_random.Next(1, 14)} days\n\nIncluded Features:\n• Unlimited mileage\n• Basic insurance coverage\n• GPS navigation system\n• 24/7 roadside assistance\n\nRemember to bring your valid driver's license and credit card.\n\nEnjoy your journey!\n\n{company} Car Rentals")
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