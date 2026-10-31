namespace Zipper.Emails;

/// <summary>
/// Represents a planned node in a deterministic email conversation thread tree.
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

    public required EmailCategory Category { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public required DateTime SentDate { get; init; }

    public bool IsHighPriority { get; init; }

    public bool RequestReadReceipt { get; init; }

    public string? ReplyTo { get; init; }
}

/// <summary>
/// Pre-plans and manages deterministic email conversation threads for an archive or corpus.
/// </summary>
internal sealed class EmailThreadPlan
{
    private static readonly object SyncRoot = new();

    private readonly object planLock = new();
    private readonly FileGenerationRequest request;
    private readonly long seedVal;
    private readonly BatesSequence? batesSequence;
    private readonly Random rng;
    private readonly Dictionary<long, EmailThreadNode> nodesByIndex = new();

    private long nextIndexToPlan = 1;
    private int nextThreadId = 1;

    public EmailThreadPlan(FileGenerationRequest request)
    {
        this.request = request;
        this.seedVal = request.Metadata.Seed ?? 42L;
        this.batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;
        // Seed the thread planner generator deterministically from the request seed.
#pragma warning disable S2245
        this.rng = new Random(unchecked((int)(this.seedVal * 397 + 1013)));
#pragma warning restore S2245

        long initialCount = Math.Max(request.Output.FileCount, 1);
        this.PlanUpTo(initialCount);
    }

    public static EmailThreadPlan GetOrCreate(FileGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ThreadPlan is not null)
        {
            return request.ThreadPlan;
        }

        lock (SyncRoot)
        {
            if (request.ThreadPlan is not null)
            {
                return request.ThreadPlan;
            }

            var plan = new EmailThreadPlan(request);
            request.ThreadPlan = plan;
            return plan;
        }
    }

    public EmailThreadNode GetNode(long index)
    {
        lock (this.planLock)
        {
            if (!this.nodesByIndex.TryGetValue(index, out var node))
            {
                this.PlanUpTo(index);
                node = this.nodesByIndex[index];
            }

            return node;
        }
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

    private void PlanUpTo(long targetIndex)
    {
        var referenceDate = this.request.Metadata.Seed.HasValue
            ? new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc)
            : DateTime.UtcNow;

        while (this.nextIndexToPlan <= targetIndex)
        {
            int threadId = this.nextThreadId++;
            long startIdx = this.nextIndexToPlan;

            int roll = this.rng.Next(100);
            int length = roll switch
            {
                < 25 => 1,
                < 65 => 2,
                < 85 => 3,
                < 95 => 4,
                _ => 5,
            };

            var category = EmailFactory.GetRandomCategory(this.rng);
            var templates = EmailFactory.GetTemplatesForCategory(category);
            var baseTemplate = templates[this.rng.Next(templates.Count)];

            int rootSenderIndex = (int)startIdx;
            int rootRecipientIndex = (int)startIdx;

            string rootSubject = EmailFactory.GenerateSubject(baseTemplate.Subject, rootRecipientIndex, rootSenderIndex, this.rng, referenceDate);
            string rootBody = EmailFactory.GenerateBody(baseTemplate.Body, rootRecipientIndex, rootSenderIndex, this.rng, referenceDate);
            DateTime rootSentDate = EmailFactory.GenerateSentDate(category, this.rng, referenceDate);
            string? rootCc = EmailFactory.GenerateCcAddresses(category, rootRecipientIndex, this.rng);
            bool rootIsHighPriority = EmailFactory.ShouldBeHighPriority(category, this.rng);
            bool rootRequestReadReceipt = EmailFactory.ShouldRequestReadReceipt(category, this.rng);
            string? rootReplyTo = EmailFactory.GenerateReplyToAddress(rootSenderIndex, category, this.rng);

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
                Category = category,
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
                long currentIndex = startIdx + m;

                int parentOffset = m == 1 ? 0 : (this.rng.Next(100) < 75 ? (m - 1) : this.rng.Next(0, m));
                var parent = threadNodes[parentOffset];

                bool isForward = this.rng.Next(100) < 15;
                int senderIdx = parent.RecipientIndex;
                int recipientIdx = isForward ? (int)currentIndex + 10 : parent.SenderIndex;

                string subject = isForward ? $"FW: {rootSubject}" : $"Re: {rootSubject}";
                string body = isForward ? BuildForwardBody(parent, this.rng) : BuildReplyBody(parent, this.rng);
                DateTime sentDate = parent.SentDate.AddMinutes(this.rng.Next(15, 1440));

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
                    Cc = EmailFactory.GenerateCcAddresses(category, recipientIdx, this.rng),
                    Category = category,
                    Subject = subject,
                    Body = body,
                    SentDate = sentDate,
                    IsHighPriority = EmailFactory.ShouldBeHighPriority(category, this.rng),
                    RequestReadReceipt = EmailFactory.ShouldRequestReadReceipt(category, this.rng),
                    ReplyTo = EmailFactory.GenerateReplyToAddress(senderIdx, category, this.rng),
                };

                threadNodes.Add(childNode);
            }

            var parentIndices = new HashSet<long>(threadNodes.Where(n => n.ParentIndex.HasValue).Select(n => n.ParentIndex!.Value));
            foreach (var node in threadNodes)
            {
                node.IsInclusive = !parentIndices.Contains(node.Index);
                this.nodesByIndex[node.Index] = node;
            }

            this.nextIndexToPlan = startIdx + length;
        }
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
        return $"{snippet}\r\n\r\nOn {parent.SentDate:ddd, dd MMM yyyy HH:mm:ss zzz}, {senderAddress} wrote:\r\n> {firstLine}";
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
        return $"{snippet}\r\n\r\n---------- Forwarded message ---------\r\nFrom: {senderAddress}\r\nDate: {parent.SentDate:ddd, dd MMM yyyy HH:mm:ss zzz}\r\nSubject: {parent.Subject}\r\nTo: {recipientAddress}\r\n\r\n{parent.Body}";
    }
}
