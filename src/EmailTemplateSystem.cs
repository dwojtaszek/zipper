namespace Zipper
{
    /// <summary>
    /// Provides predefined email templates for generating realistic test data.
    /// </summary>
    public static class EmailTemplateSystem
    {
        /// <summary>
        /// Gets a random email template from the available categories.
        /// </summary>
        /// <param name="recipientIndex">Index used to generate recipient email.</param>
        /// <param name="senderIndex">Index used to generate sender email.</param>
        /// <param name="category">Optional category constraint.</param>
        /// <returns>EmailTemplate with realistic content.</returns>
        public static EmailTemplate GetRandomTemplate(int recipientIndex, int senderIndex, EmailCategory? category = null)
            => EmailFactory.Create(recipientIndex, senderIndex, category, Random.Shared);

        /// <summary>
        /// Gets a template suitable for specific file generation contexts.
        /// </summary>
        /// <param name="context">Context information for template generation.</param>
        /// <returns>EmailTemplate tailored to the context.</returns>
        public static EmailTemplate GetContextualTemplate(EmailContext context)
            => EmailFactory.CreateContextual(context, Random.Shared);

        /// <summary>
        /// Generates a realistic email address from an index and type.
        /// </summary>
        /// <param name="index">Index used to derive the domain and user portion.</param>
        /// <param name="type">Address type prefix (e.g. "sender", "recipient").</param>
        /// <returns>A formatted email address string.</returns>
        public static string GenerateEmailAddress(int index, string type)
            => EmailFactory.GenerateEmailAddress(index, type);
    }

    /// <summary>
    /// Context information for generating contextual email templates.
    /// </summary>
    public record EmailContext
    {
        public int RecipientIndex { get; init; }

        public int SenderIndex { get; init; }

        public EmailCategory Category { get; init; }

        public int TemplateIndex { get; init; }

        public DateTime? SentDate { get; init; }

        public bool? IsHighPriority { get; init; }

        public bool? RequestReadReceipt { get; init; }

        public string? RecipientType { get; init; }

        public string? SenderType { get; init; }
    }
}
