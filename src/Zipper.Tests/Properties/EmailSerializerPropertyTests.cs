using System.Text;
using System.Text.RegularExpressions;
using FsCheck.Xunit;
using Xunit;
using Zipper.Emails;

namespace Zipper.Tests.Properties;

[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class EmailSerializerPropertyTests
{
    [Property]
    public void ToEml_WithThreadHeaders_ShouldPreserveAncestorOrder(EmailThreadHeadersPropertyInput input)
    {
        string rootMessageId = $"<msg.{input.Seed:x8}.{input.RootIndex:D8}@example.com>";
        string parentMessageId = $"<msg.{input.Seed:x8}.{input.RootIndex + 1:D8}@example.com>";
        string messageId = $"<msg.{input.Seed:x8}.{input.RootIndex + 2:D8}@example.com>";
        string references = $"{rootMessageId} {parentMessageId}";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Re: Threaded Message",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Threaded message body.",
            MessageId = messageId,
            InReplyTo = parentMessageId,
            References = references,
        };

        string content = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, null));
        string[] actualThreadHeaders = content.Split("\r\n", StringSplitOptions.None)
            .Where(line => line.StartsWith("Message-ID:", StringComparison.Ordinal)
                || line.StartsWith("In-Reply-To:", StringComparison.Ordinal)
                || line.StartsWith("References:", StringComparison.Ordinal))
            .ToArray();
        string[] expectedThreadHeaders =
        [
            $"Message-ID: {messageId}",
            $"In-Reply-To: {parentMessageId}",
            $"References: {references}",
        ];

        Assert.Equal(expectedThreadHeaders, actualThreadHeaders);
    }

    [Property]
    public void ToEml_WithRfc2047Subject_ShouldRoundTripAndKeepEncodedWordsWithinLimit(Rfc2047SubjectPropertyInput input)
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = input.Subject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "RFC 2047 property test.",
        };
        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            EncodedWords = true,
            HeaderFolding = input.Fold,
        };

        string message = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, null, new Random(input.Seed), options));
        string subjectHeader = GetSubjectHeader(message);
        string unfoldedSubject = Rfc2047Helper.UnfoldHeader(subjectHeader);
        var encodedWords = Regex.Matches(
            unfoldedSubject,
            @"=\?[^?]+\?[BbQq]\?[^?]*\?=",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        Assert.Equal(input.Subject, Rfc2047Helper.Decode(subjectHeader));
        Assert.NotEmpty(encodedWords);
        Assert.All(encodedWords, word => Assert.InRange(word.Length, 1, 75));
    }

    private static string GetSubjectHeader(string message)
    {
        string[] lines = message[..message.IndexOf("\r\n\r\n", StringComparison.Ordinal)]
            .Split("\r\n", StringSplitOptions.None);
        int subjectIndex = Array.FindIndex(lines, line => line.StartsWith("Subject:", StringComparison.Ordinal));
        Assert.True(subjectIndex >= 0, "Expected a Subject header.");

        var value = new StringBuilder(lines[subjectIndex]["Subject:".Length..].TrimStart());
        for (int i = subjectIndex + 1; i < lines.Length && lines[i].StartsWith(' '); i++)
        {
            value.Append("\r\n").Append(lines[i]);
        }

        return value.ToString().TrimStart();
    }
}
