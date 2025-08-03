using System;
using System.Text;

namespace Zipper
{
    public static class EmlFile
    {
        public static byte[] CreateEmlContent(string to, string from, string subject, DateTime sentDate, string body, (string filename, byte[] content)? attachment = null)
        {
            var boundary = attachment.HasValue ? "----=" + Guid.NewGuid().ToString("N") : "";
            var sb = new StringBuilder();

            // Headers
            sb.AppendLine($"From: {from}");
            sb.AppendLine($"To: {to}");
            sb.AppendLine($"Subject: {subject}");
            sb.AppendLine($"Date: {sentDate:ddd, dd MMM yyyy HH:mm:ss zzz}");
            sb.AppendLine("MIME-Version: 1.0");

            if (attachment.HasValue)
            {
                sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
                sb.AppendLine();
                sb.AppendLine($"--{boundary}");
            }

            // Body
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine(body);

            // Attachment
            if (attachment.HasValue)
            {
                sb.AppendLine($"--{boundary}");
                sb.AppendLine($"Content-Type: application/octet-stream; name=\"{attachment.Value.filename}\"");
                sb.AppendLine("Content-Transfer-Encoding: base64");
                sb.AppendLine($"Content-Disposition: attachment; filename=\"{attachment.Value.filename}\"");
                sb.AppendLine();
                sb.AppendLine(Convert.ToBase64String(attachment.Value.content, Base64FormattingOptions.InsertLineBreaks));
                sb.AppendLine($"--{boundary}--");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}