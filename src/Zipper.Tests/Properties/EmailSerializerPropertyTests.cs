using System.Text;
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
}
