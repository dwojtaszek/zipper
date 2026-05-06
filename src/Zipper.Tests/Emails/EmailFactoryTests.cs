using Xunit;
using Xunit.Abstractions;
using Zipper.Emails;

namespace Zipper
{
    public class EmailFactoryTests
    {
        private readonly ITestOutputHelper output;

        public EmailFactoryTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Create_Deterministic_ForSameSeed()
        {
            // Arrange
            const int recipientIndex = 7;
            const int senderIndex = 7;
            const EmailCategory category = EmailCategory.Business;
            const int seed = 42;

            // Act
            var t1 = EmailFactory.Create(recipientIndex, senderIndex, category, new Random(seed));
            var t2 = EmailFactory.Create(recipientIndex, senderIndex, category, new Random(seed));

            // Assert — all fields except SentDate (which uses DateTime.Now as base) must be identical
            Assert.Equal(t1.To, t2.To);
            Assert.Equal(t1.From, t2.From);
            Assert.Equal(t1.Subject, t2.Subject);
            Assert.Equal(t1.Body, t2.Body);
            Assert.Equal(t1.Cc, t2.Cc);
            Assert.Equal(t1.ReplyTo, t2.ReplyTo);
            Assert.Equal(t1.IsHighPriority, t2.IsHighPriority);
            Assert.Equal(t1.RequestReadReceipt, t2.RequestReadReceipt);

            // SentDate offset from now must match (same seed → same day/hour/minute adjustments)
            var diff = Math.Abs((t1.SentDate - t2.SentDate).TotalSeconds);
            Assert.True(diff < 5, $"SentDate differed by {diff:F1}s between two calls with the same seed");
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(42, 99)]
        [InlineData(999999, 888888)]
        public void Create_ProducesValidAddresses(int recipientIndex, int senderIndex)
        {
            // Act
            var template = EmailFactory.Create(recipientIndex, senderIndex, EmailCategory.Business, new Random(1));

            // Assert — basic RFC-5321 shape: local@domain
            Assert.Matches(@"^[^@]+@[^@]+\.[^@]+$", template.To);
            Assert.Matches(@"^[^@]+@[^@]+\.[^@]+$", template.From);
            Assert.Contains($"recipient{recipientIndex:D3}@", template.To);
            Assert.Contains($"sender{senderIndex:D3}@", template.From);
        }

        [Theory]
        [InlineData(EmailCategory.Notification, 7)]
        [InlineData(EmailCategory.Support, 14)]
        [InlineData(EmailCategory.Business, 60)]
        [InlineData(EmailCategory.Technical, 45)]
        [InlineData(EmailCategory.Marketing, 90)]
        [InlineData(EmailCategory.Legal, 180)]
        [InlineData(EmailCategory.Financial, 45)]
        [InlineData(EmailCategory.Personal, 30)]
        [InlineData(EmailCategory.Healthcare, 90)]
        [InlineData(EmailCategory.Education, 120)]
        [InlineData(EmailCategory.Ecommerce, 60)]
        [InlineData(EmailCategory.Travel, 365)]
        public void Create_SentDate_WithinCategoryRange(EmailCategory category, int maxDaysAgo)
        {
            // Arrange
            var now = DateTime.Now;
            const int iterations = 50;

            for (int i = 0; i < iterations; i++)
            {
                // Act
                var template = EmailFactory.Create(i, i, category, new Random(i));

                // Assert — date is not absurdly far in the past or future
                // Allow 2-day buffer for the ±23 hour and ±59 min adjustments
                Assert.True(
                    template.SentDate >= now.AddDays(-(maxDaysAgo + 2)),
                    $"SentDate {template.SentDate:O} is earlier than the maximum range for {category} ({maxDaysAgo} days)");

                Assert.True(
                    template.SentDate <= now.AddDays(1),
                    $"SentDate {template.SentDate:O} is unexpectedly far in the future for {category}");
            }
        }

        [Theory]
        [InlineData(EmailCategory.Business, 0.6)]
        [InlineData(EmailCategory.Technical, 0.4)]
        [InlineData(EmailCategory.Legal, 0.5)]
        [InlineData(EmailCategory.Financial, 0.4)]
        [InlineData(EmailCategory.Support, 0.3)]
        [InlineData(EmailCategory.Marketing, 0.8)]
        [InlineData(EmailCategory.Healthcare, 0.3)]
        [InlineData(EmailCategory.Education, 0.5)]
        [InlineData(EmailCategory.Ecommerce, 0.2)]
        [InlineData(EmailCategory.Travel, 0.4)]
        [InlineData(EmailCategory.Personal, 0.2)]
        [InlineData(EmailCategory.Notification, 0.2)]
        public void Create_CcProbability_MatchesCategorySpec(EmailCategory category, double expectedRate)
        {
            const int n = 10_000;
            int withCc = 0;
            var rng = new Random(99);

            for (int i = 0; i < n; i++)
            {
                var t = EmailFactory.Create(i, i, category, rng);
                if (t.Cc != null)
                {
                    withCc++;
                }
            }

            double actual = (double)withCc / n;
            double chiSquare = ComputeBinomialChiSquare(withCc, n, expectedRate);

            this.output.WriteLine($"Category: {category}, Expected CC rate: {expectedRate:P0}, Actual: {actual:P2}, χ²={chiSquare:F3}");

            // p ≥ 0.01 corresponds to chi-square < 6.635 for df=1
            Assert.True(chiSquare < 6.635, $"CC probability for {category} deviates significantly: χ²={chiSquare:F3} (limit 6.635 at p=0.01), actual={actual:P2} vs expected={expectedRate:P0}");
        }

        [Theory]
        [InlineData(EmailCategory.Support, 0.7)]
        [InlineData(EmailCategory.Marketing, 0.8)]
        [InlineData(EmailCategory.Business, 0.3)]
        [InlineData(EmailCategory.Technical, 0.4)]
        [InlineData(EmailCategory.Healthcare, 0.2)]
        [InlineData(EmailCategory.Education, 0.4)]
        [InlineData(EmailCategory.Ecommerce, 0.6)]
        [InlineData(EmailCategory.Travel, 0.3)]
        [InlineData(EmailCategory.Personal, 0.1)]
        [InlineData(EmailCategory.Legal, 0.1)]
        [InlineData(EmailCategory.Financial, 0.1)]
        [InlineData(EmailCategory.Notification, 0.1)]
        public void Create_ReplyToProbability_MatchesCategorySpec(EmailCategory category, double expectedRate)
        {
            const int n = 10_000;
            int withReplyTo = 0;
            var rng = new Random(77);

            for (int i = 0; i < n; i++)
            {
                var t = EmailFactory.Create(i, i, category, rng);
                if (t.ReplyTo != null)
                {
                    withReplyTo++;
                }
            }

            double actual = (double)withReplyTo / n;
            double chiSquare = ComputeBinomialChiSquare(withReplyTo, n, expectedRate);

            this.output.WriteLine($"Category: {category}, Expected ReplyTo rate: {expectedRate:P0}, Actual: {actual:P2}, χ²={chiSquare:F3}");

            Assert.True(chiSquare < 6.635, $"ReplyTo probability for {category} deviates significantly: χ²={chiSquare:F3} (limit 6.635 at p=0.01), actual={actual:P2} vs expected={expectedRate:P0}");
        }

        [Theory]
        [InlineData(EmailCategory.Legal, 0.3)]
        [InlineData(EmailCategory.Financial, 0.2)]
        [InlineData(EmailCategory.Support, 0.1)]
        [InlineData(EmailCategory.Technical, 0.15)]
        [InlineData(EmailCategory.Business, 0.05)]
        [InlineData(EmailCategory.Personal, 0.05)]
        [InlineData(EmailCategory.Marketing, 0.05)]
        [InlineData(EmailCategory.Notification, 0.05)]
        [InlineData(EmailCategory.Healthcare, 0.05)]
        [InlineData(EmailCategory.Education, 0.05)]
        [InlineData(EmailCategory.Ecommerce, 0.05)]
        [InlineData(EmailCategory.Travel, 0.05)]
        public void Create_HighPriority_Rate_MatchesCategorySpec(EmailCategory category, double expectedRate)
        {
            const int n = 10_000;
            int highPriority = 0;
            var rng = new Random(55);

            for (int i = 0; i < n; i++)
            {
                var t = EmailFactory.Create(i, i, category, rng);
                if (t.IsHighPriority)
                {
                    highPriority++;
                }
            }

            double actual = (double)highPriority / n;
            double chiSquare = ComputeBinomialChiSquare(highPriority, n, expectedRate);

            this.output.WriteLine($"Category: {category}, Expected high-priority rate: {expectedRate:P0}, Actual: {actual:P2}, χ²={chiSquare:F3}");

            Assert.True(chiSquare < 6.635, $"HighPriority rate for {category} deviates significantly: χ²={chiSquare:F3} (limit 6.635 at p=0.01), actual={actual:P2} vs expected={expectedRate:P0}");
        }

        [Theory]
        [InlineData(EmailCategory.Legal, 0.7)]
        [InlineData(EmailCategory.Financial, 0.6)]
        [InlineData(EmailCategory.Business, 0.3)]
        [InlineData(EmailCategory.Personal, 0.1)]
        [InlineData(EmailCategory.Technical, 0.1)]
        [InlineData(EmailCategory.Marketing, 0.1)]
        [InlineData(EmailCategory.Notification, 0.1)]
        [InlineData(EmailCategory.Support, 0.1)]
        [InlineData(EmailCategory.Healthcare, 0.1)]
        [InlineData(EmailCategory.Education, 0.1)]
        [InlineData(EmailCategory.Ecommerce, 0.1)]
        [InlineData(EmailCategory.Travel, 0.1)]
        public void Create_ReadReceipt_Rate_MatchesCategorySpec(EmailCategory category, double expectedRate)
        {
            const int n = 10_000;
            int withReadReceipt = 0;
            var rng = new Random(33);

            for (int i = 0; i < n; i++)
            {
                var t = EmailFactory.Create(i, i, category, rng);
                if (t.RequestReadReceipt)
                {
                    withReadReceipt++;
                }
            }

            double actual = (double)withReadReceipt / n;
            double chiSquare = ComputeBinomialChiSquare(withReadReceipt, n, expectedRate);

            this.output.WriteLine($"Category: {category}, Expected read-receipt rate: {expectedRate:P0}, Actual: {actual:P2}, χ²={chiSquare:F3}");

            Assert.True(chiSquare < 6.635, $"ReadReceipt rate for {category} deviates significantly: χ²={chiSquare:F3} (limit 6.635 at p=0.01), actual={actual:P2} vs expected={expectedRate:P0}");
        }

        [Fact]
        public void Create_PipelineEntryPoint_MatchesIndexOverload()
        {
            // The FileWorkItem pipeline entry point must produce the same result
            // as the index-based overload (same derivation: recipientIndex = senderIndex = item.Index).
            var item = new FileWorkItem { Index = 17 };
            var request = new FileGenerationRequest();
            var rng1 = new Random(123);
            var rng2 = new Random(123);

            var fromPipeline = EmailFactory.Create(item, request, rng1);
            var fromIndices = EmailFactory.Create(17, 17, category: null, rng2);

            Assert.Equal(fromPipeline.To, fromIndices.To);
            Assert.Equal(fromPipeline.From, fromIndices.From);
            Assert.Equal(fromPipeline.Subject, fromIndices.Subject);
            Assert.Equal(fromPipeline.Body, fromIndices.Body);
        }

        [Fact]
        public void Create_NullRandom_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EmailFactory.Create(0, 0, null, null!));
        }

        [Fact]
        public void Create_NullItem_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EmailFactory.Create(null!, new FileGenerationRequest(), new Random()));
        }

        [Fact]
        public void Create_NullRequest_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EmailFactory.Create(new FileWorkItem(), null!, new Random()));
        }

        [Fact]
        public void GenerateEmailAddress_ProducesExpectedFormat()
        {
            Assert.Contains("recipient042@", EmailFactory.GenerateEmailAddress(42, "recipient"));
            Assert.Contains("sender001@", EmailFactory.GenerateEmailAddress(1, "sender"));
        }

        // Tests redistributed from EmailTemplateSystemTests
        [Fact]
        public void Create_BasicParameters_ReturnsValidEmail()
        {
            const int recipientIndex = 1;
            const int senderIndex = 2;

            var email = EmailFactory.Create(recipientIndex, senderIndex, null, new Random(42));

            Assert.NotNull(email);
            Assert.False(string.IsNullOrEmpty(email.To));
            Assert.False(string.IsNullOrEmpty(email.From));
            Assert.False(string.IsNullOrEmpty(email.Subject));
            Assert.False(string.IsNullOrEmpty(email.Body));
            Assert.True(email.SentDate <= DateTime.Now);
        }

        [Theory]
        [InlineData(EmailCategory.Business)]
        [InlineData(EmailCategory.Personal)]
        [InlineData(EmailCategory.Technical)]
        [InlineData(EmailCategory.Marketing)]
        [InlineData(EmailCategory.Legal)]
        [InlineData(EmailCategory.Financial)]
        [InlineData(EmailCategory.Notification)]
        [InlineData(EmailCategory.Support)]
        [InlineData(EmailCategory.Healthcare)]
        [InlineData(EmailCategory.Education)]
        [InlineData(EmailCategory.Ecommerce)]
        [InlineData(EmailCategory.Travel)]
        public void Create_WithSpecificCategory_ReturnsCategorySpecificEmail(EmailCategory category)
        {
            const int recipientIndex = 10;
            const int senderIndex = 20;

            var email = EmailFactory.Create(recipientIndex, senderIndex, category, new Random(99));

            Assert.NotNull(email);
            Assert.Contains($"recipient{recipientIndex:D3}@", email.To);
            Assert.Contains($"sender{senderIndex:D3}@", email.From);
            this.output.WriteLine($"Category: {category}, Subject: {email.Subject}");
        }

        [Fact]
        public void Create_MultipleCalls_ReturnsVariedContent()
        {
            const int iterations = 50;
            var subjects = new string[iterations];

            for (int i = 0; i < iterations; i++)
            {
                var email = EmailFactory.Create(i + 1, i + 2, null, Random.Shared);
                subjects[i] = email.Subject;
            }

            var uniqueSubjects = subjects.Distinct().Count();
            Assert.True(uniqueSubjects > 1, "Expected multiple different subjects");
            Assert.True(uniqueSubjects >= iterations * 0.3, $"Expected at least 30% variety, got {uniqueSubjects}/{iterations}");
        }

        [Fact]
        public void Create_WithDifferentIndices_GeneratesUniqueAddresses()
        {
            const int iterations = 20;
            var emailAddresses = new (string to, string from)[iterations];

            for (int i = 0; i < iterations; i++)
            {
                var email = EmailFactory.Create(i, i * 2, null, new Random(i));
                emailAddresses[i] = (email.To, email.From);
            }

            var uniqueToAddresses = emailAddresses.Select(e => e.to).Distinct().Count();
            var uniqueFromAddresses = emailAddresses.Select(e => e.from).Distinct().Count();

            Assert.Equal(iterations, uniqueToAddresses);
            Assert.Equal(iterations, uniqueFromAddresses);
        }

        [Fact]
        public void CreateContextual_WithValidContext_ReturnsAppropriateEmail()
        {
            var context = new EmailContext
            {
                RecipientIndex = 5,
                SenderIndex = 10,
                Category = EmailCategory.Business,
                TemplateIndex = 0,
                SentDate = new DateTime(2023, 6, 15),
                IsHighPriority = true,
                RequestReadReceipt = true,
            };

            var email = EmailFactory.CreateContextual(context, new Random(42));

            Assert.NotNull(email);
            Assert.Contains("recipient005@", email.To);
            Assert.Contains("sender010@", email.From);
            Assert.Equal(new DateTime(2023, 6, 15), email.SentDate);
            Assert.True(email.IsHighPriority);
            Assert.True(email.RequestReadReceipt);
        }

        [Fact]
        public void EmailContext_RecordType_WorksCorrectly()
        {
            var context1 = new EmailContext
            {
                RecipientIndex = 1,
                SenderIndex = 2,
                Category = EmailCategory.Business,
                TemplateIndex = 0,
            };

            var context2 = context1 with { };

            Assert.Equal(context1.RecipientIndex, context2.RecipientIndex);
            Assert.Equal(context1.SenderIndex, context2.SenderIndex);
            Assert.Equal(context1.Category, context2.Category);
            Assert.Equal(context1.TemplateIndex, context2.TemplateIndex);
            Assert.True(context1 == context2);
        }

        [Fact]
        public void Create_BusinessCategory_ContainsBusinessContent()
        {
            var email = EmailFactory.Create(1, 2, EmailCategory.Business, new Random(42));

            Assert.NotNull(email);
            Assert.True(
                email.Subject.Contains("Business") ||
                email.Subject.Contains("Contract") ||
                email.Subject.Contains("Meeting") ||
                email.Subject.Contains("Partnership") ||
                email.Subject.Contains("Budget") ||
                email.Subject.Contains("Review") ||
                email.Subject.Contains("Proposal"),
                $"Expected business-related subject, got: {email.Subject}");
        }

        [Theory]
        [InlineData(EmailCategory.Healthcare)]
        [InlineData(EmailCategory.Education)]
        [InlineData(EmailCategory.Ecommerce)]
        [InlineData(EmailCategory.Travel)]
        public void Create_NewCategories_HaveRealisticPlaceholdersReplaced(EmailCategory category)
        {
            var bodyPlaceholders = new[]
            {
                "{recipient}", "{sender}", "{company}", "{department}", "{project}",
                "{amount}", "{deadline}", "{meeting}", "{date}", "{place}", "{venue}",
            };

            for (int i = 0; i < 5; i++)
            {
                var email = EmailFactory.Create(i + 1, i + 2, category, new Random(i));
                foreach (var placeholder in bodyPlaceholders)
                {
                    Assert.DoesNotContain(placeholder, email.Body);
                }
            }
        }

        [Fact]
        public void Create_WithLargeIndices_HandlesCorrectly()
        {
            const int largeRecipientIndex = 999999;
            const int largeSenderIndex = 888888;

            var email = EmailFactory.Create(largeRecipientIndex, largeSenderIndex, null, new Random(1));

            Assert.NotNull(email);
            Assert.Contains($"recipient{largeRecipientIndex:D6}@", email.To);
            Assert.Contains($"sender{largeSenderIndex:D6}@", email.From);
        }

        [Fact]
        public void Create_BodyReplacements_AllPlaceholdersReplaced()
        {
            var commonPlaceholders = new[] { "{recipient}", "{sender}", "{date}", "{quarter}" };
            var bodyOnlyPlaceholders = new[] { "{company}", "{department}", "{project}", "{amount}", "{deadline}", "{meeting}" };

            for (int i = 0; i < 50; i++)
            {
                var email = EmailFactory.Create(i + 1, i + 2, null, new Random(i));
                foreach (var placeholder in commonPlaceholders)
                {
                    Assert.DoesNotContain(placeholder, email.Subject);
                    Assert.DoesNotContain(placeholder, email.Body);
                }

                foreach (var placeholder in bodyOnlyPlaceholders)
                {
                    Assert.DoesNotContain(placeholder, email.Body);
                }
            }
        }

        private static double ComputeBinomialChiSquare(int observed, int n, double expectedRate)
        {
            double expectedYes = n * expectedRate;
            double expectedNo = n * (1.0 - expectedRate);
            double observedNo = n - observed;

            // Guard against division by zero for extreme rates
            if (expectedYes < 1.0 || expectedNo < 1.0)
            {
                return 0.0;
            }

            return ((observed - expectedYes) * (observed - expectedYes) / expectedYes)
                 + ((observedNo - expectedNo) * (observedNo - expectedNo) / expectedNo);
        }
    }
}
