using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Emails;

namespace Zipper.Tests;

public class MimeRealismTests
{
    [Fact]
    public void MimeRealismOptions_Parse_HandlesValidTokensAndPresets()
    {
        var disabled = MimeRealismOptions.Parse(null);
        Assert.False(disabled.IsEnabled);

        var disabledExplicit = MimeRealismOptions.Parse("none");
        Assert.False(disabledExplicit.IsEnabled);

        var standard = MimeRealismOptions.Parse("standard");
        Assert.True(standard.IsEnabled);
        Assert.True(standard.EncodedWords);
        Assert.True(standard.HeaderFolding);
        Assert.True(standard.QuotedPrintableBody);
        Assert.True(standard.MultipartAlternative);
        Assert.True(standard.MultiAttachment);
        Assert.False(standard.Base64Body);
        Assert.False(standard.NestedRfc822);
        Assert.False(standard.DeliberatelyNonRfc);
        Assert.Equal(2, standard.MaxNestingDepth);

        var full = MimeRealismOptions.Parse("full");
        Assert.True(full.IsEnabled);
        Assert.True(full.EncodedWords);
        Assert.True(full.HeaderFolding);
        Assert.True(full.QuotedPrintableBody);
        Assert.True(full.Base64Body);
        Assert.True(full.MultipartAlternative);
        Assert.True(full.MultiAttachment);
        Assert.True(full.NestedRfc822);
        Assert.True(full.DeliberatelyNonRfc);
        Assert.Equal(2, full.MaxNestingDepth);

        var custom = MimeRealismOptions.Parse("encoded-words,nested-rfc822,non-rfc");
        Assert.True(custom.IsEnabled);
        Assert.True(custom.EncodedWords);
        Assert.False(custom.QuotedPrintableBody);
        Assert.True(custom.NestedRfc822);
        Assert.True(custom.DeliberatelyNonRfc);
    }

    [Fact]
    public void MimeRealismOptions_TryValidate_EmptyTokenLists_ReturnsFalse()
    {
        Assert.False(MimeRealismOptions.TryValidate(",", out var error1));
        Assert.NotNull(error1);

        Assert.False(MimeRealismOptions.TryValidate(",,,", out var error2));
        Assert.NotNull(error2);

        Assert.False(MimeRealismOptions.TryValidate("qp,", out var error3));
        Assert.NotNull(error3);

        Assert.False(MimeRealismOptions.TryValidate(",qp", out var error4));
        Assert.NotNull(error4);

        Assert.False(MimeRealismOptions.Parse(",").IsEnabled);
    }

    [Fact]
    public void ToEml_WithEncodedWords_EncodesNonAsciiSubjectAndDecodesRoundTrip()
    {
        const string originalSubject = "Projektprüfung & Résumé: ñoño";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = originalSubject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body with accents.",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            EncodedWords = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Subject: =?utf-8?", emlText, StringComparison.Ordinal);

        // Extract the Subject line
        var lines = emlText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        var subjectLine = lines.First(l => l.StartsWith("Subject: ", StringComparison.Ordinal))["Subject: ".Length..];

        var decoded = Rfc2047Helper.Decode(subjectLine);
        Assert.Equal(originalSubject, decoded);
    }

    [Fact]
    public void ToEml_WithEncodedWordsAndLongSingleWord_FoldsSubjectHeader()
    {
        const string originalSubject = "界界界界界界界界界界界界界界界";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = originalSubject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body.",
        };
        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            EncodedWords = true,
            HeaderFolding = true,
        };

        string emlText = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, null, seeded: null, options));
        string[] lines = emlText.Split("\r\n", StringSplitOptions.None);
        int subjectIndex = Array.FindIndex(lines, line => line.StartsWith("Subject:", StringComparison.Ordinal));

        Assert.True(subjectIndex >= 0, "Expected a Subject header.");
        var subjectLines = lines.Skip(subjectIndex).TakeWhile((line, index) => index == 0 || line.StartsWith(' ')).ToArray();
        Assert.True(subjectLines.Length >= 2, "Expected folded subject header.");
        Assert.All(subjectLines, line => Assert.InRange(line.Length, 1, 76));
        var fullSubject = string.Join("\r\n", subjectLines);
        Assert.Equal(originalSubject, Rfc2047Helper.Decode(Rfc2047Helper.UnfoldHeader(fullSubject)["Subject:".Length..].Trim()));
    }

    [Fact]
    public void ToEml_WithHeaderFolding_FoldsLongSubject()
    {
        var longSubject = "This is an extremely long email subject intended to test RFC 5322 and RFC 2047 header folding behavior across multiple continuation lines in MIME serializers";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = longSubject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body.",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            HeaderFolding = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        // Folded headers use CRLF followed by a space or tab
        Assert.Contains("\r\n ", emlText, StringComparison.Ordinal);
        var unfolded = Rfc2047Helper.UnfoldHeader(emlText);
        Assert.Contains($"Subject: {longSubject}", unfolded, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithHeaderFolding_FoldsLongReferencesHeader()
    {
        const string references =
            "<root.000000000000000000000000@example.test> <reply.000000000000000000000000@example.test>";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Thread reply",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body.",
            References = references,
        };
        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            HeaderFolding = true,
        };

        string emlText = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, null, seeded: null, options));
        string[] lines = emlText.Split("\r\n", StringSplitOptions.None);
        int referencesIndex = Array.FindIndex(lines, line => line.StartsWith("References:", StringComparison.Ordinal));

        Assert.True(referencesIndex >= 0, "Expected a References header.");
        Assert.All(
            lines.Skip(referencesIndex).TakeWhile((line, index) => index == 0 || line.StartsWith(' ')),
            line => Assert.InRange(line.Length, 1, 76));
        Assert.Equal($"References: {references}", Rfc2047Helper.UnfoldHeader(emlText)
            .Split("\r\n", StringSplitOptions.None)
            .First(line => line.StartsWith("References:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ToEml_WithDeliberatelyNonRfc_EmitsNonRfcEncodedWordVariant()
    {
        var nonAsciiSubject = "Aviso urgente sobre la facturación electrónica del periodo";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = nonAsciiSubject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body.",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            EncodedWords = true,
            DeliberatelyNonRfc = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        // Non-RFC variant has unspaced adjacent encoded-words without linear whitespace
        Assert.Contains("?==?", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithQuotedPrintableBody_EncodesBodyAndSetsHeader()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "QP Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Line with accents: café, résumé, naïve & special chars = 100%! " + new string('x', 100),
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            QuotedPrintableBody = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Transfer-Encoding: quoted-printable", emlText, StringComparison.Ordinal);
        Assert.Contains("=3D", emlText, StringComparison.Ordinal); // '=' encoded as =3D
        string encodedBody = emlText.Split("\r\n\r\n", 2, StringSplitOptions.None)[1];
        Assert.Contains("=\r\n", encodedBody, StringComparison.Ordinal);
        Assert.All(encodedBody.Split("\r\n", StringSplitOptions.None), line => Assert.InRange(line.Length, 0, 76));
    }

    [Fact]
    public void ToEml_WithBase64Body_EncodesBodyAndSetsHeader()
    {
        string bodyText = "Secret message content to be encoded in base64 format. " + new string('x', 100);
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Base64 Body Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = bodyText,
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            Base64Body = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Transfer-Encoding: base64", emlText, StringComparison.Ordinal);
        var expectedBase64 = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(bodyText),
            Base64FormattingOptions.InsertLineBreaks);
        Assert.Contains(expectedBase64, emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithFullMimeRealism_EmitsBothBodyTransferEncodings()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Full MIME realism",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Body with café and enough content to exercise body transfer encodings.",
        };

        var encodings = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 32; seed++)
        {
            var eml = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, null, new Random(seed), MimeRealismOptions.Full));
            foreach (var line in eml.Split("\r\n", StringSplitOptions.None))
            {
                if (line.StartsWith("Content-Transfer-Encoding: ", StringComparison.Ordinal))
                {
                    encodings.Add(line["Content-Transfer-Encoding: ".Length..]);
                }
            }
        }

        Assert.Contains("quoted-printable", encodings);
        Assert.Contains("base64", encodings);
    }

    [Fact]
    public void ToEml_WithMultipartAlternative_EmitsTextAndHtmlParts()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Multipart Alternative Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Plain text version of the message.",
            HtmlBody = "<html><body><p>HTML version of the message.</p></body></html>",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            MultipartAlternative = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: multipart/alternative; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/html; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Plain text version of the message.", emlText, StringComparison.Ordinal);
        Assert.Contains("<html><body><p>HTML version of the message.</p></body></html>", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMultipartAlternativeAndAttachment_EmitsNestedMultipartMixedAndAlternative()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Mixed with Alternative and Attachment",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Plain text version.",
            HtmlBody = "<html><body><p>HTML version.</p></body></html>",
        };

        var attachment = new EmailAttachment
        {
            FileName = "report.pdf",
            Content = new byte[] { 0x25, 0x50, 0x44, 0x46 },
            ContentType = "application/pdf",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            MultipartAlternative = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, attachment, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: multipart/mixed; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: multipart/alternative; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/html; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/pdf; name=\"report.pdf\"", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMultiAttachment_EmitsMultipleAttachments()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Multiple Attachments Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Here are two attachments.",
        };

        var attachments = new[]
        {
            new EmailAttachment
            {
                FileName = "doc1.pdf",
                Content = new byte[] { 1, 2, 3 },
                ContentType = "application/pdf",
            },
            new EmailAttachment
            {
                FileName = "image2.jpg",
                Content = new byte[] { 4, 5, 6 },
                ContentType = "image/jpeg",
            },
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            MultiAttachment = true,
        };

        var emlBytes = EmailSerializer.ToEmlWithAttachments(email, attachments, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: multipart/mixed; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: attachment; filename=\"doc1.pdf\"", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: attachment; filename=\"image2.jpg\"", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithNestedRfc822_EmitsAttachedEmailPart()
    {
        var childEmail = new Email
        {
            To = "sub-recipient@example.com",
            From = "sub-sender@example.com",
            Subject = "Forwarded Inner Email",
            SentDate = new DateTime(2024, 1, 14, 9, 0, 0, DateTimeKind.Utc),
            Body = "Original inner discussion.",
        };
        var childBytes = EmailSerializer.ToEml(childEmail, null, new Random(111));

        var parentEmail = new Email
        {
            To = "parent-recipient@example.com",
            From = "parent-sender@example.com",
            Subject = "Fwd: Forwarded Inner Email",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Please find the attached forwarded email.",
        };

        var nestedAttachment = new EmailAttachment
        {
            FileName = "forwarded.eml",
            Content = childBytes,
            ContentType = "message/rfc822",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            NestedRfc822 = true,
        };

        var emlBytes = EmailSerializer.ToEml(parentEmail, nestedAttachment, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: message/rfc822; name=\"forwarded.eml\"", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: 8bit", emlText, StringComparison.Ordinal);
        Assert.Contains("Subject: Forwarded Inner Email", emlText, StringComparison.Ordinal);
        Assert.Contains("To: sub-recipient@example.com", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmlFileGenerator_WithMimeRealism_ProducesDeterministicOutputForSameSeed()
    {
        var generator = new EmlFileGenerator();
        var request1 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 3, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 555, MimeRealism = "standard" },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };
        var request2 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 3, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 555, MimeRealism = "standard" },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };

        for (long i = 1; i <= 3; i++)
        {
            var workItem = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var result1 = generator.Generate(workItem, request1);
            var result2 = generator.Generate(workItem, request2);

            Assert.Equal(result1.Content, result2.Content);
        }
    }

    [Fact]
    public void EmlFileGenerator_WithNestedRfc822_EmitsNestedMessagesWithinDepthLimit()
    {
        const int maxNestingDepth = 2;
        const int fileCount = 128;
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = fileCount, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 12345, MimeRealism = "nested-rfc822" },
            LoadFile = new LoadFileConfig { AttachmentRate = 0 },
        };

        var nestedPartCounts = new List<int>(fileCount);
        for (long i = 1; i <= fileCount; i++)
        {
            var item = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var eml = Encoding.UTF8.GetString(generator.Generate(item, request).Content);
            nestedPartCounts.Add(CountOccurrences(eml, "Content-Type: message/rfc822;"));
        }

        Assert.Contains(nestedPartCounts, count => count > 0);
        Assert.Contains(nestedPartCounts, count => count == maxNestingDepth);
        Assert.All(nestedPartCounts, count => Assert.InRange(count, 0, maxNestingDepth));
    }

    [Fact]
    public void EmlFileGenerator_WithNestedRfc822AndFullAttachmentRate_PreservesRateSelectedPlaceholder()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 32, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 12345, MimeRealism = "nested-rfc822" },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };

        for (long i = 1; i <= 32; i++)
        {
            var item = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var result = generator.Generate(item, request);
            string eml = Encoding.UTF8.GetString(result.Content);

            Assert.NotNull(result.Attachment);
            Assert.DoesNotContain("Content-Type: message/rfc822;", eml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EmlFileGenerator_WithNestedRfc822_UsesSeededReferenceDateForNestedEmails()
    {
        const int fileCount = 128;
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = fileCount, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 12345, MimeRealism = "nested-rfc822" },
            LoadFile = new LoadFileConfig { AttachmentRate = 0 },
        };

        FileWorkItem? nestedItem = null;
        byte[]? firstContents = null;
        for (long i = 1; i <= fileCount; i++)
        {
            var item = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var contents = generator.Generate(item, request).Content;
            if (Encoding.UTF8.GetString(contents).Contains("Content-Type: message/rfc822;", StringComparison.Ordinal))
            {
                nestedItem = item;
                firstContents = contents;
                break;
            }
        }

        Assert.NotNull(nestedItem);
        Assert.NotNull(firstContents);
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        var secondContents = generator.Generate(nestedItem, request).Content;
        Assert.Equal(firstContents, secondContents);
    }

    [Fact]
    public void EmlFileGenerator_WithFullMimeRealism_ProducesByteIdenticalOutputForSameSeed()
    {
        var generator = new EmlFileGenerator();
        var request1 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 5, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 888, MimeRealism = "full" },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };
        var request2 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 5, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 888, MimeRealism = "full" },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };

        for (long i = 1; i <= 5; i++)
        {
            var item = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var result1 = generator.Generate(item, request1);
            var result2 = generator.Generate(item, request2);

            Assert.Equal(result1.Content, result2.Content);
        }
    }

    [Fact]
    public void EmlFileGenerator_WithoutMimeRealism_MaintainsLegacyGoldenBytes()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = false },
        };
        var workItem = new FileWorkItem { Index = 1, FileName = "test.eml", FolderName = "f", FolderNumber = 1 };
        var result = generator.Generate(workItem, request);

        // Golden SHA-256 baseline
        Assert.Equal(
            "2577c72e5c61570d9e8e638b109068bed4087e3533bfbc67086421cf899f7c91",
            Convert.ToHexString(SHA256.HashData(result.Content)).ToLowerInvariant());
    }

    [Fact]
    public void DatProductionComposer_WithMultipleAttachments_SetsEndAttachToFinalChildForParentAndAllChildren()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            Production = new ProductionConfig { ProductionSet = true },
        };

        var attachments = new List<(string filename, byte[] content)>
        {
            ("first.pdf", new byte[] { 1, 2 }),
            ("second.docx", new byte[] { 3, 4 }),
        };

        var workItem = new FileWorkItem { Index = 1, FileName = "00000001.eml", FolderName = "f1", FolderNumber = 1 };
        var fileData = new FileData
        {
            WorkItem = workItem,
            AttachmentPayloads = attachments,
            AttachmentMetadata = FileAttachmentMetadata.FromPayloads(attachments, request, workItem),
        };

        var composer = new global::Zipper.LoadFiles.DatComposer(request, global::Zipper.LoadFiles.WriterMode.ProductionSet);
        var records = composer.Compose(new[] { fileData }).ToList();

        Assert.Equal(3, records.Count);
        var headers = composer.HeaderColumns.ToList();
        int begAttachIdx = headers.IndexOf("BEGATTACH");
        int endAttachIdx = headers.IndexOf("ENDATTACH");

        Assert.True(begAttachIdx >= 0);
        Assert.True(endAttachIdx >= 0);

        string expectedBeg = "DOC00000001";
        string expectedEnd = "DOC00000001_A002";

        // Parent
        Assert.Equal(expectedBeg, records[0].Values[begAttachIdx]);
        Assert.Equal(expectedEnd, records[0].Values[endAttachIdx]);

        // Child 1
        Assert.Equal(expectedBeg, records[1].Values[begAttachIdx]);
        Assert.Equal(expectedEnd, records[1].Values[endAttachIdx]);

        // Child 2
        Assert.Equal(expectedBeg, records[2].Values[begAttachIdx]);
        Assert.Equal(expectedEnd, records[2].Values[endAttachIdx]);
    }

    [Fact]
    public async Task ZipArchiveSink_WithDuplicateAttachmentFilenames_EmitsDistinctArchivePaths()
    {
        var tempZip = Path.GetTempFileName() + ".zip";
        var tempDat = Path.GetTempFileName() + ".dat";
        try
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig { FileType = "eml", FileCount = 1, Concurrency = 1, WithText = true },
                Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
                LoadFile = new LoadFileConfig(),
            };

            var attachments = new List<(string filename, byte[] content)>
            {
                ("report.pdf", new byte[] { 1, 2 }),
                ("report.pdf", new byte[] { 3, 4 }),
            };

            var workItem = new FileWorkItem { Index = 1, FileName = "00000001.eml", FilePathInZip = "00000001.eml", FolderName = "f1", FolderNumber = 1 };
            var fileData = new FileData
            {
                WorkItem = workItem,
                Data = Encoding.ASCII.GetBytes("From: a@b.com\r\nSubject: Test\r\n\r\nBody"),
                AttachmentPayloads = attachments,
                AttachmentMetadata = FileAttachmentMetadata.FromPayloads(attachments, request, workItem),
            };

            var channel = System.Threading.Channels.Channel.CreateUnbounded<FileData>();
            await channel.Writer.WriteAsync(fileData);
            channel.Writer.Complete();

            var sink = new ZipArchiveSink();
            await sink.CreateArchiveAsync(tempZip, "test.dat", tempDat, request, channel.Reader);

            using var archive = System.IO.Compression.ZipFile.OpenRead(tempZip);
            var entryNames = archive.Entries.Select(e => e.FullName).ToList();

            Assert.Contains("f1/1_report.pdf", entryNames);
            Assert.Contains("f1/1_report_A002.pdf", entryNames);
            Assert.Contains("f1/1_report.txt", entryNames);
            Assert.Contains("f1/1_report_A002.txt", entryNames);

            // Also verify StandardRowComposer output matches these paths
            var composer = new global::Zipper.LoadFiles.DatComposer(request, global::Zipper.LoadFiles.WriterMode.Standard);
            var records = composer.Compose(new[] { fileData }).ToList();
            Assert.Equal(3, records.Count);
            var headers = composer.HeaderColumns.ToList();
            int fileCol = headers.IndexOf("File Path");
            Assert.Equal("f1/1_report.pdf", records[1].Values[fileCol]);
            Assert.Equal("f1/1_report_A002.pdf", records[2].Values[fileCol]);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }

            if (File.Exists(tempDat))
            {
                File.Delete(tempDat);
            }
        }
    }

    [Fact]
    public async Task ZipArchiveSink_WithAttachmentsSharingStemDifferentExtensions_EmitsDistinctTextAndNativePaths()
    {
        var tempZip = Path.GetTempFileName() + ".zip";
        var tempDat = Path.GetTempFileName() + ".dat";
        try
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig { FileType = "eml", FileCount = 1, WithText = true },
                Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
                LoadFile = new LoadFileConfig { Formats = [LoadFileFormat.Dat] },
            };

            var attachments = new List<(string filename, byte[] content)>
            {
                ("report.pdf", new byte[] { 1, 2, 3 }),
                ("report.docx", new byte[] { 4, 5, 6 }),
            };

            var workItem = new FileWorkItem { Index = 1, FileName = "00000001.eml", FilePathInZip = "00000001.eml", FolderName = "f1", FolderNumber = 1 };
            var fileData = new FileData
            {
                WorkItem = workItem,
                Data = Encoding.ASCII.GetBytes("From: a@b.com\r\nSubject: Test\r\n\r\nBody"),
                AttachmentPayloads = attachments,
                AttachmentMetadata = FileAttachmentMetadata.FromPayloads(attachments, request, workItem),
            };

            var channel = System.Threading.Channels.Channel.CreateUnbounded<FileData>();
            await channel.Writer.WriteAsync(fileData);
            channel.Writer.Complete();

            var sink = new ZipArchiveSink();
            await sink.CreateArchiveAsync(tempZip, "test.dat", tempDat, request, channel.Reader);

            using var archive = System.IO.Compression.ZipFile.OpenRead(tempZip);
            var entryNames = archive.Entries.Select(e => e.FullName).ToList();

            Assert.Contains("f1/1_report.pdf", entryNames);
            Assert.Contains("f1/1_report.docx", entryNames);
            Assert.Contains("f1/1_report.txt", entryNames);
            Assert.Contains("f1/1_report_A002.txt", entryNames);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }

            if (File.Exists(tempDat))
            {
                File.Delete(tempDat);
            }
        }
    }

    [Fact]
    public void Rfc2047Helper_EncodeSubject_LongAsciiWithoutSpaces_DoesNotStartWithBareLineFeed()
    {
        var longAscii = new string('a', 80);
        var encoded = Rfc2047Helper.EncodeSubject(longAscii, fold: true);

        Assert.False(encoded.StartsWith('\n'));
        Assert.False(encoded.StartsWith("\r\n", StringComparison.Ordinal));
        Assert.Contains(longAscii, encoded);
    }

    [Fact]
    public void QuotedPrintableHelper_Encode_SpacePrecedingSoftBreak_IsEncodedAsHex()
    {
        var input = new string('a', 74) + " hello";
        var encoded = QuotedPrintableHelper.Encode(input);

        Assert.DoesNotContain(" =\r\n", encoded);
    }

    [Fact]
    public async Task XmlLoadFileWriter_WithDuplicateAttachmentFilenames_EmitsDistinctChildNativePaths()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            LoadFile = new LoadFileConfig { Formats = [LoadFileFormat.EdrmXml] },
        };

        var attachments = new List<(string filename, byte[] content)>
        {
            ("report.pdf", new byte[] { 1, 2 }),
            ("report.pdf", new byte[] { 3, 4 }),
        };

        var workItem = new FileWorkItem { Index = 1, FileName = "00000001.eml", FilePathInZip = "00000001.eml", FolderName = "f1", FolderNumber = 1 };
        var fileData = new FileData
        {
            WorkItem = workItem,
            Data = Encoding.ASCII.GetBytes("From: a@b.com\r\nSubject: Test\r\n\r\nBody"),
            AttachmentPayloads = attachments,
            AttachmentMetadata = FileAttachmentMetadata.FromPayloads(attachments, request, workItem),
        };

        var writer = new global::Zipper.LoadFiles.XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, request, new[] { fileData });

        var xmlContent = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("1_report.pdf", xmlContent);
        Assert.Contains("1_report_A002.pdf", xmlContent);
    }

    [Fact]
    public async Task StandardRowComposer_And_XmlLoadFileWriter_InProductionSetMode_AlignChildNativePaths()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1, WithText = true },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            Production = new ProductionConfig { ProductionSet = true },
            LoadFile = new LoadFileConfig { Formats = [LoadFileFormat.EdrmXml] },
        };

        var attachments = new List<(string filename, byte[] content)>
        {
            ("first.pdf", new byte[] { 1, 2 }),
            ("second.pdf", new byte[] { 3, 4 }),
        };

        var workItem = new FileWorkItem { Index = 1, FileName = "DOC00000001.eml", FilePathInZip = "NATIVES/VOL001/DOC00000001.eml", FolderName = "VOL001", FolderNumber = 1, BatesNumberOverride = "DOC00000001" };
        var fileData = new FileData
        {
            WorkItem = workItem,
            Data = Encoding.ASCII.GetBytes("From: a@b.com\r\nSubject: Test\r\n\r\nBody"),
            AttachmentPayloads = attachments,
            AttachmentMetadata = FileAttachmentMetadata.FromPayloads(attachments, request, workItem),
        };

        // XML
        var xmlWriter = new global::Zipper.LoadFiles.XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await xmlWriter.WriteAsync(stream, request, new[] { fileData });
        var xmlContent = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("NATIVES/VOL001/DOC00000001_A001.pdf", xmlContent);
        Assert.Contains("NATIVES/VOL001/DOC00000001_A002.pdf", xmlContent);
        Assert.Contains("TEXT/VOL001/DOC00000001_A001.txt", xmlContent);
        Assert.Contains("TEXT/VOL001/DOC00000001_A002.txt", xmlContent);

        // CSV (StandardRowComposer)
        var csvComposer = new global::Zipper.LoadFiles.CsvComposer(request);
        var records = csvComposer.Compose(new[] { fileData }).ToList();
        Assert.Equal(3, records.Count);
        Assert.Contains("NATIVES/VOL001/DOC00000001_A001.pdf", records[1].Values);
        Assert.Contains("NATIVES/VOL001/DOC00000001_A002.pdf", records[2].Values);
    }

    [Fact]
    public async Task XmlLoadFileWriter_SimulatedHash_DistinctAttachmentsAcrossDocuments_DoNotCollide()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 2 },
            Metadata = new MetadataConfig { Seed = 42, WithFamilies = true },
            Hash = new HashConfig { Mode = HashMode.Simulated, Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5 } },
            LoadFile = new LoadFileConfig { Formats = [LoadFileFormat.EdrmXml] },
        };

        var doc5Attachments = new List<(string filename, byte[] content)>
        {
            ("first.pdf", new byte[] { 1 }),
            ("second.pdf", new byte[] { 2 }),
        };
        var doc6Attachments = new List<(string filename, byte[] content)>
        {
            ("first.pdf", new byte[] { 3 }),
        };

        var workItem5 = new FileWorkItem { Index = 5, FileName = "00000005.eml", FilePathInZip = "00000005.eml", FolderName = "f1", FolderNumber = 1 };
        var fileData5 = new FileData
        {
            WorkItem = workItem5,
            Data = Encoding.ASCII.GetBytes("From: a@b.com\r\n\r\nBody"),
            AttachmentPayloads = doc5Attachments,
            AttachmentMetadata = FileAttachmentMetadata.FromPayloads(doc5Attachments, request, workItem5),
        };

        var workItem6 = new FileWorkItem { Index = 6, FileName = "00000006.eml", FilePathInZip = "00000006.eml", FolderName = "f1", FolderNumber = 1 };
        var fileData6 = new FileData
        {
            WorkItem = workItem6,
            Data = Encoding.ASCII.GetBytes("From: a@b.com\r\n\r\nBody"),
            AttachmentPayloads = doc6Attachments,
            AttachmentMetadata = FileAttachmentMetadata.FromPayloads(doc6Attachments, request, workItem6),
        };

        var xmlWriter = new global::Zipper.LoadFiles.XmlLoadFileWriter();
        using var stream = new MemoryStream();
        await xmlWriter.WriteAsync(stream, request, new[] { fileData5, fileData6 });
        stream.Position = 0;

        var doc = System.Xml.Linq.XDocument.Load(stream);
        var doc5Att2 = doc.Descendants("Document").FirstOrDefault(d => (string?)d.Attribute("DocID") == "DOC00000005_A002");
        var doc6Att1 = doc.Descendants("Document").FirstOrDefault(d => (string?)d.Attribute("DocID") == "DOC00000006_A001");

        Assert.NotNull(doc5Att2);
        Assert.NotNull(doc6Att1);

        var hash5 = doc5Att2.Descendants("ExternalFile").FirstOrDefault()?.Attribute("Hash")?.Value;
        var hash6 = doc6Att1.Descendants("ExternalFile").FirstOrDefault()?.Attribute("Hash")?.Value;

        Assert.NotNull(hash5);
        Assert.NotNull(hash6);
        Assert.NotEqual(hash5, hash6);
    }

    private static int CountOccurrences(string value, string searched)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(searched, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += searched.Length;
        }

        return count;
    }
}
