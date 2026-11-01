using System.Text;
using Xunit;

namespace Zipper.Tests.Emails;

public class EmailAnomalyApplierTests
{
    private const string Boundary = "----=_Part_12345";
    private const string EncodedWordCharsetMarker = "unknown-8859-99";

    private static readonly Encoding Lossless = Encoding.Latin1;

    private static byte[] MultipartEmail() => Lossless.GetBytes(string.Join(
        "\r\n",
        "From: alice@example.com",
        "To: bob@example.com",
        "Subject: Quarterly report",
        "Date: Mon, 01 Jan 2024 10:00:00 +0000",
        "MIME-Version: 1.0",
        $"Content-Type: multipart/mixed; boundary=\"{Boundary}\"",
        string.Empty,
        $"--{Boundary}",
        "Content-Type: text/plain; charset=utf-8",
        string.Empty,
        "Body text",
        $"--{Boundary}",
        "Content-Type: application/octet-stream; name=\"a.bin\"",
        "Content-Transfer-Encoding: base64",
        string.Empty,
        "AAEC",
        $"--{Boundary}--",
        string.Empty));

    private static byte[] SinglePartEmail() => Lossless.GetBytes(string.Join(
        "\r\n",
        "From: alice@example.com",
        "To: bob@example.com",
        "Subject: Quarterly report",
        "MIME-Version: 1.0",
        "Content-Type: text/plain; charset=utf-8",
        "Content-Transfer-Encoding: 8bit",
        string.Empty,
        "Body text",
        string.Empty));

    private static EmailAnomalyResult Apply(byte[] eml, string type, int seed = 1, long index = 1) =>
        EmailAnomalyApplier.Apply(eml, "DOC00000001.eml", "DOC00000001", type, index, new Random(seed));

    private static string Text(byte[] bytes) => Lossless.GetString(bytes);

    [Fact]
    public void Apply_MimeBoundary_AcrossSeeds_ProducesHeaderMismatchOrUnclosedDelimiter()
    {
        var variantsSeen = new HashSet<string>(StringComparer.Ordinal);
        var originalLineCount = Text(MultipartEmail()).Split("\r\n").Length;

        for (int seed = 1; seed <= 30; seed++)
        {
            var result = Apply(MultipartEmail(), "mime-boundary", seed);
            string text = Text(result.Content);
            var lines = text.Split("\r\n");

            // Replacements keep the physical line count, so the audit line number points at real content.
            Assert.Equal(originalLineCount, lines.Length);
            Assert.Equal("mime-boundary", result.Anomaly.ErrorType);

            if (text.Contains($"boundary=\"{Boundary}_MISMATCH\"", StringComparison.Ordinal))
            {
                variantsSeen.Add("header-mismatch");
                Assert.Contains($"--{Boundary}", lines);
                Assert.DoesNotContain($"--{Boundary}_MISMATCH", lines);
                Assert.Equal("Content-Type", result.Anomaly.Column);
            }
            else
            {
                variantsSeen.Add("unclosed-delimiter");
                Assert.Contains($"--{Boundary}", lines);
                Assert.DoesNotContain($"--{Boundary}--", lines);
                Assert.Equal("MIME-Boundary", result.Anomaly.Column);
            }
        }

        Assert.Equal(2, variantsSeen.Count);
    }

    [Fact]
    public void Apply_MimeBoundary_SinglePartMessage_DeclaresBoundaryAbsentFromBody()
    {
        var result = Apply(SinglePartEmail(), "mime-boundary");
        var lines = Text(result.Content).Split("\r\n");

        Assert.Contains(lines, line => line.StartsWith("Content-Type: multipart/mixed; boundary=", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("--", StringComparison.Ordinal));
        Assert.Equal("5", result.Anomaly.LineNumber);
        Assert.Equal("Content-Type", result.Anomaly.Column);
    }

    [Fact]
    public void Apply_EncodedWord_AcrossSeeds_ReplacesSubjectWithMalformedEncodedWord()
    {
        var variantsSeen = new HashSet<string>(StringComparer.Ordinal);

        for (int seed = 1; seed <= 30; seed++)
        {
            var result = Apply(MultipartEmail(), "encoded-word", seed);
            var lines = Text(result.Content).Split("\r\n");
            string subject = lines.Single(line => line.StartsWith("Subject:", StringComparison.Ordinal));

            Assert.DoesNotContain("Quarterly report", subject, StringComparison.Ordinal);
            Assert.StartsWith("Subject: =?", subject, StringComparison.Ordinal);

            if (subject.Contains(EncodedWordCharsetMarker, StringComparison.Ordinal))
            {
                variantsSeen.Add("unknown-charset");
            }
            else if (subject.Contains("abc===", StringComparison.Ordinal))
            {
                variantsSeen.Add("bad-base64-padding");
            }
            else
            {
                Assert.Contains("dGV zdA==", subject, StringComparison.Ordinal);
                variantsSeen.Add("whitespace-in-encoded-word");
            }

            Assert.Equal("encoded-word", result.Anomaly.ErrorType);
            Assert.Equal("Subject", result.Anomaly.Column);
            Assert.Equal("3", result.Anomaly.LineNumber);
        }

        Assert.Equal(3, variantsSeen.Count);
    }

    [Fact]
    public void Apply_EncodedWord_WhenSubjectIsMissing_InsertsMalformedSubjectHeader()
    {
        var eml = Lossless.GetBytes("From: alice@example.com\r\nMIME-Version: 1.0\r\n\r\nBody\r\n");

        var result = Apply(eml, "encoded-word");
        var lines = Text(result.Content).Split("\r\n");

        Assert.Contains(lines, line => line.StartsWith("Subject: =?", StringComparison.Ordinal));
        Assert.Equal("3", result.Anomaly.LineNumber);
    }

    [Fact]
    public void Apply_HeaderFold_AcrossSeeds_InjectsIllegalFoldingOrBareLineBreak()
    {
        var variantsSeen = new HashSet<string>(StringComparer.Ordinal);

        for (int seed = 1; seed <= 30; seed++)
        {
            var result = Apply(MultipartEmail(), "header-fold", seed);
            string text = Text(result.Content);

            if (text.Contains("\nBareLFInHeader", StringComparison.Ordinal))
            {
                variantsSeen.Add("bare-lf");
            }
            else if (text.Contains("\rBareCRInHeader", StringComparison.Ordinal))
            {
                variantsSeen.Add("bare-cr");
            }
            else
            {
                // Illegal fold: the original Subject value is intact once the injected CRLF is removed,
                // and the continuation line has no leading linear whitespace.
                Assert.Contains("Subject: Quarterly report", text.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
                var continuation = text.Split("\r\n").Skip(3).First();
                Assert.False(continuation.Length > 0 && (continuation[0] == ' ' || continuation[0] == '\t'));
                variantsSeen.Add("illegal-fold");
            }

            Assert.Equal("header-fold", result.Anomaly.ErrorType);
            Assert.Equal("Subject", result.Anomaly.Column);
        }

        Assert.Equal(3, variantsSeen.Count);
    }

    [Fact]
    public void Apply_Rfc822Depth_WrapsMessageInTwentyFiveNestedRfc822Parts()
    {
        var result = Apply(MultipartEmail(), "rf822-depth");
        string text = Text(result.Content);

        Assert.Equal(25, CountOccurrences(text, "Content-Type: message/rfc822"));
        Assert.Equal(25, CountOccurrences(text, "boundary=\"----=_Chaos_Depth_1_"));
        Assert.Contains("Subject: Forwarded Level 1", text, StringComparison.Ordinal);
        Assert.Contains("Subject: Quarterly report", text, StringComparison.Ordinal);
        Assert.Equal("rf822-depth", result.Anomaly.ErrorType);
        Assert.Equal("1", result.Anomaly.LineNumber);
    }

    [Fact]
    public void Apply_NonUtf8Payload_SurvivesEveryType()
    {
        byte[] payload = { 0xFF, 0xFE, 0x80, 0x00 };
        var eml = MultipartEmail().Concat(payload).ToArray();

        foreach (var type in ChaosAnomalyTypes.Email)
        {
            var result = Apply(eml, type);

            Assert.True(
                result.Content.AsSpan().IndexOf(payload) >= 0,
                $"Payload bytes were altered by '{type}'.");
        }
    }

    [Fact]
    public void Apply_SameInputSeedAndIndex_ProducesIdenticalOutputAndAudit()
    {
        foreach (var type in ChaosAnomalyTypes.Email)
        {
            var first = Apply(MultipartEmail(), type, seed: 42, index: 7);
            var second = Apply(MultipartEmail(), type, seed: 42, index: 7);

            Assert.Equal(first.Content, second.Content);
            Assert.Equal(first.Anomaly, second.Anomaly);
        }
    }

    [Fact]
    public void Apply_Rfc822Depth_NestedBoundariesDependOnWorkItemIndex()
    {
        var first = Apply(MultipartEmail(), "rf822-depth", index: 1);
        var second = Apply(MultipartEmail(), "rf822-depth", index: 2);

        Assert.NotEqual(first.Content, second.Content);
    }

    [Fact]
    public void Apply_UnknownType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Apply(MultipartEmail(), "not-a-type"));
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
