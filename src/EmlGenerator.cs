namespace Zipper
{
    /// <summary>
    /// Legacy EML generator for backward compatibility
    /// Note: Consider using EmailBuilder for new implementations.
    /// </summary>
    public static class EmlGenerator
    {
        /// <summary>
        /// Creates EML content using the legacy interface.
        /// </summary>
        /// <param name="to">Recipient email address.</param>
        /// <param name="from">Sender email address.</param>
        /// <param name="subject">Email subject.</param>
        /// <param name="sentDate">When the email was sent.</param>
        /// <param name="body">Email body content.</param>
        /// <param name="attachment">Optional attachment information.</param>
        /// <returns>Byte array representing the EML file content.</returns>
        public static byte[] CreateEmlContent(string to, string from, string subject, DateTime sentDate, string body, (string filename, byte[] content)? attachment = null)
        {
            // Delegate to the new EmailBuilder for actual implementation
            return EmailBuilder.BuildEmail(to, from, subject, sentDate, body, attachment);
        }
    }
}
