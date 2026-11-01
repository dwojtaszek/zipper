using System.Text;
using Xunit;

namespace Zipper.Tests.Emails;

public class EmailAnomalyApplierTests
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private static byte[] CreateSampleMultipartEmail(string boundary = "----=_Part_12345")
    {
        var sb = new StringBuilder();
        sb.Append("From: alice@example.com\r\n");
        sb.Append("To: bob@example.com\r\n");
        sb.Append("Subject: Test Multipart Email\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n");
        sb.Append("\r\n");
        sb.Append($"--{boundary}\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("\r\n");
        sb.Append("Hello Bob,\r\nThis is a multipart email.\r\n");
        sb.Append($"--{boundary}\r\n");
        sb.Append("Content-Type: application/pdf; name=\"doc.pdf\"\r\n");
        sb.Append("Content-Disposition: attachment; filename=\"doc.pdf\"\r\n");
        sb.Append("\r\n");
        sb.Append("JVBERi0xLjQKfakecontent\r\n");
        sb.Append($"--{boundary}--\r\n");
        return Utf8.GetBytes(sb.ToString());
    }

    private static byte[] CreateSampleSimpleEmail()
    {
        var sb = new StringBuilder();
        sb.Append("From: alice@example.com\r\n");
        sb.Append("To: bob@example.com\r\n");
        sb.Append("Subject: Simple Test Email\r\n");
        sb.Append("Date: Mon, 01 Jan 2024 10:00:00 +0000\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("\r\n");
        sb.Append("Hello Bob,\r\nThis is a simple plain text email.\r\n");
        return Utf8.GetBytes(sb.ToString());
    }

    [Fact]
    public void ChaosAnomalyTypes_Email_ContainsExpectedFourAnomalyTypes()
    {
        Assert.Contains("mime-boundary", ChaosAnomalyTypes.Email);
        Assert.Contains("encoded-word", ChaosAnomalyTypes.Email);
        Assert.Contains("header-fold", ChaosAnomalyTypes.Email);
        Assert.Contains("rf822-depth", ChaosAnomalyTypes.Email);
        Assert.Equal(4, ChaosAnomalyTypes.Email.Count);
    }

    [Fact]
    public void ChaosAnomalyTypes_ForFormat_WithEmailString_ReturnsEmailTypes()
    {
        Assert.Equal(ChaosAnomalyTypes.Email, ChaosAnomalyTypes.ForFormat("eml"));
        Assert.Equal(ChaosAnomalyTypes.Email, ChaosAnomalyTypes.ForFormat("email"));
    }

    [Fact]
    public void Apply_MimeBoundary_InjectsBoundaryMismatchOrMissingClose()
    {
        var applier = new EmailAnomalyApplier(new Random(42));
        var original = CreateSampleMultipartEmail();
        var anomalies = new List<ChaosAnomaly>();

        var mutated = applier.ApplyToEmail(
            original,
            fileName: "DOC00000001.eml",
            recordId: "DOC00000001",
            chaosType: "mime-boundary",
            anomalies: anomalies,
            workItemIndex: 1);

        Assert.NotEqual(original, mutated);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("DOC00000001.eml", anomaly.File);
        Assert.Equal("DOC00000001", anomaly.RecordID);
        Assert.Equal("mime-boundary", anomaly.ErrorType);
        Assert.False(string.IsNullOrEmpty(anomaly.LineNumber));
        Assert.False(string.IsNullOrEmpty(anomaly.Description));

        string mutatedText = Utf8.GetString(mutated);
        // Either boundary in Content-Type differs from body, or closing delimiter was altered
        bool hasMismatch = mutatedText.Contains("_MISMATCH", StringComparison.Ordinal)
            || !mutatedText.Contains("------=_Part_12345--", StringComparison.Ordinal);
        Assert.True(hasMismatch);
    }

    [Fact]
    public void Apply_EncodedWord_InjectsMalformedSequence()
    {
        var applier = new EmailAnomalyApplier(new Random(42));
        var original = CreateSampleSimpleEmail();
        var anomalies = new List<ChaosAnomaly>();

        var mutated = applier.ApplyToEmail(
            original,
            fileName: "DOC00000002.eml",
            recordId: "DOC00000002",
            chaosType: "encoded-word",
            anomalies: anomalies,
            workItemIndex: 2);

        Assert.NotEqual(original, mutated);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("DOC00000002.eml", anomaly.File);
        Assert.Equal("DOC00000002", anomaly.RecordID);
        Assert.Equal("Subject", anomaly.Column);
        Assert.Equal("encoded-word", anomaly.ErrorType);

        string mutatedText = Utf8.GetString(mutated);
        Assert.Contains("=?", mutatedText, StringComparison.Ordinal);
        Assert.Contains("?=", mutatedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_HeaderFold_InjectsIllegalFoldingOrBareLfCr()
    {
        var applier = new EmailAnomalyApplier(new Random(42));
        var original = CreateSampleSimpleEmail();
        var anomalies = new List<ChaosAnomaly>();

        var mutated = applier.ApplyToEmail(
            original,
            fileName: "DOC00000003.eml",
            recordId: "DOC00000003",
            chaosType: "header-fold",
            anomalies: anomalies,
            workItemIndex: 3);

        Assert.NotEqual(original, mutated);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("DOC00000003.eml", anomaly.File);
        Assert.Equal("header-fold", anomaly.ErrorType);
    }

    [Fact]
    public void Apply_Rfc822Depth_GeneratesDeeplyNestedMessageExceedingCommonLimits()
    {
        var applier = new EmailAnomalyApplier(new Random(42));
        var original = CreateSampleSimpleEmail();
        var anomalies = new List<ChaosAnomaly>();

        var mutated = applier.ApplyToEmail(
            original,
            fileName: "DOC00000004.eml",
            recordId: "DOC00000004",
            chaosType: "rf822-depth",
            anomalies: anomalies,
            workItemIndex: 4);

        Assert.NotEqual(original, mutated);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("DOC00000004.eml", anomaly.File);
        Assert.Equal("rf822-depth", anomaly.ErrorType);

        string mutatedText = Utf8.GetString(mutated);
        // Count occurrences of message/rfc822 in the mutated email
        int count = 0;
        int idx = 0;
        while ((idx = mutatedText.IndexOf("message/rfc822", idx, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            idx += "message/rfc822".Length;
        }

        // Must exceed common parser limit of 20
        Assert.True(count >= 20, $"Expected >= 20 nested message/rfc822 parts, got {count}");
    }

    [Fact]
    public void Apply_IsDeterministic_ForSameSeed()
    {
        var applier1 = new EmailAnomalyApplier(new Random(100));
        var applier2 = new EmailAnomalyApplier(new Random(100));
        var original = CreateSampleMultipartEmail();

        var anomalies1 = new List<ChaosAnomaly>();
        var anomalies2 = new List<ChaosAnomaly>();

        var mutated1 = applier1.ApplyToEmail(original, "DOC1.eml", "DOC1", "mime-boundary", anomalies1, 1);
        var mutated2 = applier2.ApplyToEmail(original, "DOC1.eml", "DOC1", "mime-boundary", anomalies2, 1);

        Assert.Equal(mutated1, mutated2);
        Assert.Equal(anomalies1, anomalies2);
    }

    [Fact]
    public void Implements_IAnomalyApplier_Interface_Safely()
    {
        IAnomalyApplier applier = new EmailAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();

        // Line-based Apply should not crash
        string line = "Subject: Hello World";
        string result = applier.Apply(1, line, "DOC1", "encoded-word", anomalies);
        Assert.NotNull(result);

        // GetEncodingAnomaly should return null without crash
        byte[]? encAnomaly = applier.GetEncodingAnomaly(1, 2, Encoding.UTF8, anomalies);
        Assert.Null(encAnomaly);
    }
}
