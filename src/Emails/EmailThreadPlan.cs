using System.Collections.Concurrent;
using System.Globalization;

namespace Zipper.Emails;

/// <summary>
/// Represents a planned node in a deterministic Email conversation thread tree.
/// </summary>
internal sealed class EmailThreadNode
{
    public required long Index { get; init; }

    public required long RootIndex { get; init; }

    public long? ParentIndex { get; init; }

    public required string ThreadDocId { get; init; }

    public required string ThreadParentDocId { get; init; }

    public required string MessageId { get; init; }

    public string? InReplyTo { get; init; }

    public string? References { get; init; }

    public bool IsInclusive { get; set; }

    public required int SenderIndex { get; init; }

    public required int RecipientIndex { get; init; }

    public string? Cc { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public required DateTime SentDate { get; init; }

    public bool IsHighPriority { get; init; }

    public bool RequestReadReceipt { get; init; }

    public string? ReplyTo { get; init; }
}

/// <summary>
/// Plans deterministic Email conversation batches on demand for an Archive or corpus.
/// </summary>
internal sealed class EmailThreadPlan
{
    private const int ThreadBatchLength = 5;
    private const int MaxCachedBatches = 32;

    private readonly long seedVal;
    private readonly BatesSequence? batesSequence;
    private readonly long emailCount;
    private readonly long firstEmailFileIndex = 1;
    private readonly int[]? sourceEmailFileIndices;
    private readonly DateTime referenceDate;
    private readonly ConcurrentDictionary<long, IReadOnlyList<EmailThreadNode>> batchCache = new();
    private readonly ConcurrentQueue<long> batchCacheOrder = new();

    public EmailThreadPlan(FileGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        this.seedVal = request.Metadata.Seed ?? 42L;
        this.batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;
        this.referenceDate = request.Metadata.Seed.HasValue
            ? new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc)
            : DateTime.UtcNow;

        if (request.SourceRecords is not null)
        {
            var emailFileIndices = new List<int>();
            for (int i = 0; i < request.SourceRecords.Count; i++)
            {
                if (string.Equals(request.SourceRecords[i].FileType, "eml", StringComparison.OrdinalIgnoreCase))
                {
                    emailFileIndices.Add(i + 1);
                }
            }

            if (emailFileIndices.Count == request.SourceRecords.Count)
            {
                this.emailCount = Math.Max(1, request.Output.FileCount);
            }
            else if (emailFileIndices.Count > 0)
            {
                this.sourceEmailFileIndices = emailFileIndices.ToArray();
                this.emailCount = emailFileIndices.Count;
            }
            else
            {
                this.emailCount = Math.Max(1, request.Output.FileCount);
            }
        }
        else if (request.Output.FileTypePlan is not null)
        {
            long precedingFileCount = 0;
            foreach (var type in request.Output.FileTypePlan.Types)
            {
                long typeCount = request.Output.FileTypePlan.GetTypeCount(type);
                if (string.Equals(type, "eml", StringComparison.OrdinalIgnoreCase))
                {
                    this.firstEmailFileIndex = precedingFileCount + 1;
                    this.emailCount = typeCount;
                    break;
                }

                precedingFileCount += typeCount;
            }

            if (this.emailCount == 0)
            {
                this.firstEmailFileIndex = 1;
                this.emailCount = Math.Max(1, request.Output.FileCount);
            }
        }
        else
        {
            this.emailCount = Math.Max(1, request.Output.FileCount);
        }
    }

    public static EmailThreadPlan GetOrCreate(FileGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return System.Threading.LazyInitializer.EnsureInitialized(
            ref request.ThreadPlan,
            () => new EmailThreadPlan(request));
    }

    public EmailThreadNode GetNode(long index)
    {
        long emailOrdinal = this.GetEmailOrdinal(index);
        if (emailOrdinal < 1 || emailOrdinal > this.emailCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        long batchStartOrdinal = ((emailOrdinal - 1) / ThreadBatchLength * ThreadBatchLength) + 1;
        int batchLength = (int)Math.Min(ThreadBatchLength, this.emailCount - batchStartOrdinal + 1);
        var batch = this.GetOrCreateBatch(batchStartOrdinal, batchLength);
        return batch[checked((int)(emailOrdinal - batchStartOrdinal))];
    }

    public Email CreateEmail(EmailThreadNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new Email
        {
            To = EmailFactory.GenerateEmailAddress(node.RecipientIndex, "recipient"),
            From = EmailFactory.GenerateEmailAddress(node.SenderIndex, "sender"),
            Subject = node.Subject,
            Body = node.Body,
            SentDate = node.SentDate,
            Cc = node.Cc,
            IsHighPriority = node.IsHighPriority,
            RequestReadReceipt = node.RequestReadReceipt,
            ReplyTo = node.ReplyTo,
            MessageId = node.MessageId,
            InReplyTo = node.InReplyTo,
            References = node.References,
            ThreadId = node.ThreadDocId,
            IsInclusive = node.IsInclusive,
            ThreadParentId = node.ThreadParentDocId,
        };
    }

    private IReadOnlyList<EmailThreadNode> PlanBatch(long startEmailOrdinal, int batchLength)
    {
#pragma warning disable S2245
        var random = new Random(unchecked((int)(this.seedVal * 397 + startEmailOrdinal * 1013)));
#pragma warning restore S2245
        var batchNodes = new List<EmailThreadNode>(batchLength);
        int plannedCount = 0;
        while (plannedCount < batchLength)
        {
            long threadStartOrdinal = startEmailOrdinal + plannedCount;
            int threadId = checked((int)threadStartOrdinal);
            long startIdx = this.GetFileIndex(threadStartOrdinal);

            int roll = random.Next(100);
            int length = roll switch
            {
                < 25 => 1,
                < 65 => 2,
                < 85 => 3,
                < 95 => 4,
                _ => 5,
            };
            length = Math.Min(length, batchLength - plannedCount);

            var category = EmailFactory.GetRandomCategory(random);
            var templates = EmailFactory.GetTemplatesForCategory(category);
            var baseTemplate = templates[random.Next(templates.Count)];

            int rootSenderIndex = (int)startIdx;
            int rootRecipientIndex = (int)startIdx;

            string rootSubject = EmailFactory.GenerateSubject(baseTemplate.Subject, rootRecipientIndex, rootSenderIndex, random, this.referenceDate);
            string rootBody = EmailFactory.GenerateBody(baseTemplate.Body, rootRecipientIndex, rootSenderIndex, random, this.referenceDate);
            DateTime rootSentDate = EmailFactory.GenerateSentDate(category, random, this.referenceDate);
            string? rootCc = EmailFactory.GenerateCcAddresses(category, rootRecipientIndex, random);
            bool rootIsHighPriority = EmailFactory.ShouldBeHighPriority(category, random);
            bool rootRequestReadReceipt = EmailFactory.ShouldRequestReadReceipt(category, random);
            string? rootReplyTo = EmailFactory.GenerateReplyToAddress(rootSenderIndex, category, random);

            string rootDocId = this.FormatDocId(startIdx);
            string rootMsgId = FormatMessageId(startIdx, threadId, rootSenderIndex);

            var rootNode = new EmailThreadNode
            {
                Index = startIdx,
                RootIndex = startIdx,
                ParentIndex = null,
                ThreadDocId = rootDocId,
                ThreadParentDocId = string.Empty,
                MessageId = rootMsgId,
                InReplyTo = null,
                References = null,
                IsInclusive = length == 1,
                SenderIndex = rootSenderIndex,
                RecipientIndex = rootRecipientIndex,
                Cc = rootCc,
                Subject = rootSubject,
                Body = rootBody,
                SentDate = rootSentDate,
                IsHighPriority = rootIsHighPriority,
                RequestReadReceipt = rootRequestReadReceipt,
                ReplyTo = rootReplyTo,
            };

            var threadNodes = new List<EmailThreadNode>(length) { rootNode };

            for (int m = 1; m < length; m++)
            {
                long currentIndex = this.GetFileIndex(threadStartOrdinal + m);

                int parentOffset = m == 1 ? 0 : (random.Next(100) < 75 ? (m - 1) : random.Next(0, m));
                var parent = threadNodes[parentOffset];

                bool isForward = random.Next(100) < 15;
                int senderIdx = parent.RecipientIndex;
                int recipientIdx = isForward ? (int)currentIndex + 10 : parent.SenderIndex;

                string subject = isForward ? $"FW: {rootSubject}" : $"Re: {rootSubject}";
                string body = isForward ? BuildForwardBody(parent, random) : BuildReplyBody(parent, random);
                DateTime sentDate = threadNodes[^1].SentDate.AddMinutes(random.Next(15, 1440));

                string childMsgId = FormatMessageId(currentIndex, threadId, senderIdx);
                string inReplyTo = parent.MessageId;
                string references = string.IsNullOrEmpty(parent.References)
                    ? parent.MessageId
                    : $"{parent.References} {parent.MessageId}";

                var childNode = new EmailThreadNode
                {
                    Index = currentIndex,
                    RootIndex = startIdx,
                    ParentIndex = parent.Index,
                    ThreadDocId = rootDocId,
                    ThreadParentDocId = this.FormatDocId(parent.Index),
                    MessageId = childMsgId,
                    InReplyTo = inReplyTo,
                    References = references,
                    IsInclusive = true,
                    SenderIndex = senderIdx,
                    RecipientIndex = recipientIdx,
                    Cc = EmailFactory.GenerateCcAddresses(category, recipientIdx, random),
                    Subject = subject,
                    Body = body,
                    SentDate = sentDate,
                    IsHighPriority = EmailFactory.ShouldBeHighPriority(category, random),
                    RequestReadReceipt = EmailFactory.ShouldRequestReadReceipt(category, random),
                    ReplyTo = EmailFactory.GenerateReplyToAddress(senderIdx, category, random),
                };

                threadNodes.Add(childNode);
            }

            var parentIndices = new HashSet<long>(threadNodes.Where(n => n.ParentIndex.HasValue).Select(n => n.ParentIndex!.Value));
            foreach (var node in threadNodes)
            {
                node.IsInclusive = !parentIndices.Contains(node.Index);
                batchNodes.Add(node);
            }

            plannedCount += length;
        }

        return batchNodes;
    }

    private long GetEmailOrdinal(long fileIndex)
    {
        if (this.sourceEmailFileIndices is not null)
        {
            if (fileIndex > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(fileIndex));
            }

            int ordinal = Array.BinarySearch(this.sourceEmailFileIndices, (int)fileIndex);
            if (ordinal < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileIndex), "The requested Source Record is not an Email.");
            }

            return ordinal + 1L;
        }

        if (fileIndex < this.firstEmailFileIndex
            || fileIndex > int.MaxValue
            || fileIndex - this.firstEmailFileIndex >= this.emailCount)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex), "The requested File Type is not an Email.");
        }

        return fileIndex - this.firstEmailFileIndex + 1;
    }

    private long GetFileIndex(long emailOrdinal)
    {
        if (this.sourceEmailFileIndices is not null)
        {
            return this.sourceEmailFileIndices[checked((int)(emailOrdinal - 1))];
        }

        return this.firstEmailFileIndex + emailOrdinal - 1;
    }

    private IReadOnlyList<EmailThreadNode> GetOrCreateBatch(long batchStartOrdinal, int batchLength)
    {
        if (this.batchCache.TryGetValue(batchStartOrdinal, out var cached))
        {
            return cached;
        }

        var planned = this.PlanBatch(batchStartOrdinal, batchLength);
        var batch = this.batchCache.GetOrAdd(batchStartOrdinal, planned);
        if (ReferenceEquals(batch, planned))
        {
            this.batchCacheOrder.Enqueue(batchStartOrdinal);
            while (this.batchCache.Count > MaxCachedBatches && this.batchCacheOrder.TryDequeue(out long expiredStartOrdinal))
            {
                this.batchCache.TryRemove(expiredStartOrdinal, out _);
            }
        }

        return batch;
    }

    private string FormatDocId(long index)
    {
        return this.batesSequence is not null
            ? this.batesSequence.Format(index - 1).ToString()
            : $"DOC{index:D8}";
    }

    private string FormatMessageId(long index, int threadId, int senderIndex)
    {
        var domain = EmailFactory.GetDomainForIndex(senderIndex);
        return $"<msg.{this.seedVal:x8}.{index:D8}.{threadId:D4}@{domain}>";
    }

    private static string BuildReplyBody(EmailThreadNode parent, Random random)
    {
        var replySnippets = new[]
        {
            "Thank you for the update. I have reviewed the details and agree with the proposed approach.",
            "Acknowledged. We are looking into this and will follow up shortly.",
            "Thanks for confirming. Let's proceed with the next steps as discussed.",
            "Please see my feedback below regarding this item.",
            "Received, thank you. Everything looks in order.",
        };

        var snippet = replySnippets[random.Next(replySnippets.Length)];
        var firstLine = parent.Body.Split('\n')[0].Trim();
        var senderAddress = EmailFactory.GenerateEmailAddress(parent.SenderIndex, "sender");
        var sentDate = parent.SentDate.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
        return $"{snippet}\r\n\r\nOn {sentDate}, {senderAddress} wrote:\r\n> {firstLine}";
    }

    private static string BuildForwardBody(EmailThreadNode parent, Random random)
    {
        var fwdSnippets = new[]
        {
            "FYI, forwarding the message below for your review.",
            "Please see the forwarded message below for context.",
            "Forwarding this for your information.",
        };

        var snippet = fwdSnippets[random.Next(fwdSnippets.Length)];
        var senderAddress = EmailFactory.GenerateEmailAddress(parent.SenderIndex, "sender");
        var recipientAddress = EmailFactory.GenerateEmailAddress(parent.RecipientIndex, "recipient");
        var sentDate = parent.SentDate.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
        return $"{snippet}\r\n\r\n---------- Forwarded message ---------\r\nFrom: {senderAddress}\r\nDate: {sentDate}\r\nSubject: {parent.Subject}\r\nTo: {recipientAddress}\r\n\r\n{parent.Body}";
    }
}
