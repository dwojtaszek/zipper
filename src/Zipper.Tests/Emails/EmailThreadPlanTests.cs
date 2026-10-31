using System.Globalization;
using Xunit;
using Zipper.Config;
using Zipper.Emails;

namespace Zipper.Tests.Emails;

public class EmailThreadPlanTests
{
    [Fact]
    public void GetNode_SingleMessageThread_HasValidRootAttributes()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
        };

        var plan = EmailThreadPlan.GetOrCreate(request);
        var node = plan.GetNode(1);

        Assert.Equal(1, node.Index);
        Assert.Equal(1, node.RootIndex);
        Assert.Null(node.ParentIndex);
        Assert.Equal("DOC00000001", node.ThreadDocId);
        Assert.Equal(string.Empty, node.ThreadParentDocId);
        Assert.StartsWith("<msg.", node.MessageId, StringComparison.Ordinal);
        Assert.EndsWith(">", node.MessageId, StringComparison.Ordinal);
        Assert.Null(node.InReplyTo);
        Assert.Null(node.References);
        Assert.True(node.IsInclusive, "The only emitted message is a leaf in the generated corpus.");
        Assert.NotEmpty(node.Subject);
        Assert.NotEmpty(node.Body);
    }

    [Fact]
    public void GetNode_MultiMessageThread_RepliesFormValidAncestryChain()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 20 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
        };

        var plan = EmailThreadPlan.GetOrCreate(request);

        // Find at least one reply node (ParentIndex != null)
        EmailThreadNode? replyNode = null;
        for (long i = 1; i <= 20; i++)
        {
            var node = plan.GetNode(i);
            if (node.ParentIndex.HasValue)
            {
                replyNode = node;
                break;
            }
        }

        Assert.NotNull(replyNode);
        Assert.NotNull(replyNode.ParentIndex);
        Assert.NotNull(replyNode.InReplyTo);
        Assert.NotNull(replyNode.References);

        var parentNode = plan.GetNode(replyNode.ParentIndex.Value);
        Assert.Equal(parentNode.MessageId, replyNode.InReplyTo);
        Assert.Contains(parentNode.MessageId, replyNode.References, StringComparison.Ordinal);
        Assert.Equal(parentNode.ThreadDocId, replyNode.ThreadDocId);
        Assert.Equal(parentNode.ThreadDocId, replyNode.ThreadParentDocId);
        Assert.True(replyNode.SentDate > parentNode.SentDate, "Reply sent date must be chronologically after parent.");
        Assert.True(replyNode.Subject.StartsWith("Re: ", StringComparison.Ordinal) || replyNode.Subject.StartsWith("FW: ", StringComparison.Ordinal));
    }

    [Fact]
    public void GetNode_MultiMessageThread_SentDatesIncreaseInMessageOrder()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 100 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
        };

        var plan = EmailThreadPlan.GetOrCreate(request);
        var nodes = Enumerable.Range(1, 100)
            .Select(index => plan.GetNode(index))
            .GroupBy(node => node.RootIndex);

        foreach (var thread in nodes)
        {
            var messages = thread.OrderBy(node => node.Index).ToList();
            for (int i = 1; i < messages.Count; i++)
            {
                Assert.True(
                    messages[i].SentDate > messages[i - 1].SentDate,
                    $"Message {messages[i].Index} must be later than message {messages[i - 1].Index} in its thread.");
            }
        }
    }

    [Fact]
    public void GetNode_SourceRecords_ThreadsOnlyEmailFileTypes()
    {
        var sourceRecords = new[]
        {
            new Zipper.SourceInput.SourceRecord { RelativePath = "one.pdf", FileType = "pdf" },
            new Zipper.SourceInput.SourceRecord { RelativePath = "two.eml", FileType = "eml" },
            new Zipper.SourceInput.SourceRecord { RelativePath = "three.pdf", FileType = "pdf" },
            new Zipper.SourceInput.SourceRecord { RelativePath = "four.eml", FileType = "eml" },
            new Zipper.SourceInput.SourceRecord { RelativePath = "five.eml", FileType = "eml" },
        };
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileType = "pdf",
                FileCount = sourceRecords.Length,
                SourceFileTypes = ["eml", "pdf"],
            },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
            SourceRecords = sourceRecords,
        };

        var plan = EmailThreadPlan.GetOrCreate(request);
        var emailNodes = new[] { plan.GetNode(2), plan.GetNode(4), plan.GetNode(5) };
        var messageIds = emailNodes.Select(node => node.MessageId).ToHashSet(StringComparer.Ordinal);

        Assert.All(emailNodes, node =>
        {
            if (node.InReplyTo is not null)
            {
                Assert.Contains(node.InReplyTo, messageIds);
                Assert.All(node.References!.Split(' '), reference => Assert.Contains(reference, messageIds));
            }
        });
    }

    [Fact]
    public void GetNode_LargeCorpus_PlansOnlyRequestedBatch()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 10_000_000 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
        };

        var node = EmailThreadPlan.GetOrCreate(request).GetNode(10_000_000);

        Assert.Equal(10_000_000, node.Index);
    }

    [Fact]
    public void GetOrCreate_ClonedRequestWithNewSeed_CreatesAnIndependentPlan()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 2 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
        };
        var originalMessageId = EmailThreadPlan.GetOrCreate(request).GetNode(1).MessageId;
        var clone = request.Clone();
        clone.Metadata = clone.Metadata with { Seed = 123 };

        var clonedMessageId = EmailThreadPlan.GetOrCreate(clone).GetNode(1).MessageId;

        Assert.NotEqual(originalMessageId, clonedMessageId);
    }

    [Fact]
    public void GetNode_Inclusiveness_LeavesAreInclusiveAndParentsAreNot()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 30 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
        };

        var plan = EmailThreadPlan.GetOrCreate(request);

        var nodes = new List<EmailThreadNode>();
        for (long i = 1; i <= 30; i++)
        {
            nodes.Add(plan.GetNode(i));
        }

        var parentIndices = nodes.Where(n => n.ParentIndex.HasValue).Select(n => n.ParentIndex!.Value).ToHashSet();

        foreach (var node in nodes)
        {
            if (parentIndices.Contains(node.Index))
            {
                Assert.False(node.IsInclusive, $"Node {node.Index} has children so must not be inclusive.");
            }
            else
            {
                Assert.True(node.IsInclusive, $"Node {node.Index} is a leaf so must be inclusive.");
            }
        }
    }

    [Fact]
    public void GetNode_Determinism_IdenticalSeedProducesIdenticalThreadGraph()
    {
        var request1 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 20 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 12345 },
        };
        var request2 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 20 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 12345 },
        };

        var plan1 = EmailThreadPlan.GetOrCreate(request1);
        var plan2 = EmailThreadPlan.GetOrCreate(request2);

        for (long i = 1; i <= 20; i++)
        {
            var node1 = plan1.GetNode(i);
            var node2 = plan2.GetNode(i);

            Assert.Equal(node1.MessageId, node2.MessageId);
            Assert.Equal(node1.InReplyTo, node2.InReplyTo);
            Assert.Equal(node1.References, node2.References);
            Assert.Equal(node1.ThreadDocId, node2.ThreadDocId);
            Assert.Equal(node1.ThreadParentDocId, node2.ThreadParentDocId);
            Assert.Equal(node1.IsInclusive, node2.IsInclusive);
            Assert.Equal(node1.Subject, node2.Subject);
            Assert.Equal(node1.SentDate, node2.SentDate);
        }
    }

    [Fact]
    public void GetNode_Determinism_IsIndependentOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var frenchCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        frenchCulture.DateTimeFormat.AbbreviatedDayNames = ["dim", "lun", "mar", "mer", "jeu", "ven", "sam"];
        frenchCulture.DateTimeFormat.AbbreviatedMonthNames = ["janv", "févr", "mars", "avr", "mai", "juin", "juil", "août", "sept", "oct", "nov", "déc", ""];

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var englishNodes = GenerateThreadNodes();

            CultureInfo.CurrentCulture = frenchCulture;
            var frenchNodes = GenerateThreadNodes();

            Assert.Equal(englishNodes.Select(node => node.Body), frenchNodes.Select(node => node.Body));
            Assert.Equal(englishNodes.Select(node => node.Subject), frenchNodes.Select(node => node.Subject));
            Assert.Equal(englishNodes.Select(node => node.SentDate), frenchNodes.Select(node => node.SentDate));
            Assert.Contains(englishNodes, node => node.Subject.StartsWith("FW: ", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        static EmailThreadNode[] GenerateThreadNodes()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig { FileType = "eml", FileCount = 20 },
                Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
            };
            var plan = EmailThreadPlan.GetOrCreate(request);
            return Enumerable.Range(1, 20).Select(index => plan.GetNode(index)).ToArray();
        }
    }

    [Fact]
    public void GetNode_ConcurrentRequests_PreserveDeterministicThreadGraph()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 500 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 12345 },
        };
        var plan = EmailThreadPlan.GetOrCreate(request);
        var messageIds = new string[500];

        Parallel.For(0, 5_000, requestIndex =>
        {
            int fileIndex = (requestIndex * 37 % messageIds.Length) + 1;
            messageIds[fileIndex - 1] = plan.GetNode(fileIndex).MessageId;
        });

        var expectedPlan = EmailThreadPlan.GetOrCreate(new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 500 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 12345 },
        });

        for (int index = 0; index < messageIds.Length; index++)
        {
            Assert.Equal(expectedPlan.GetNode(index + 1).MessageId, messageIds[index]);
        }
    }

    [Fact]
    public void GetNode_DifferentSeeds_ProduceDifferentThreadGraphs()
    {
        var request1 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 10 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 111 },
        };
        var request2 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 10 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 999 },
        };

        var plan1 = EmailThreadPlan.GetOrCreate(request1);
        var plan2 = EmailThreadPlan.GetOrCreate(request2);

        var node1 = plan1.GetNode(1);
        var node2 = plan2.GetNode(1);

        Assert.NotEqual(node1.MessageId, node2.MessageId);
    }

    [Fact]
    public void GetNode_WithSourceRecordOverrides_UsesControlNumberOrBatesForThreadDocId()
    {
        var records = new List<Zipper.SourceInput.SourceRecord>
        {
            new() { RelativePath = "eml1.eml", FileType = "eml", ControlNumber = "CUSTOM-ROOT" },
            new() { RelativePath = "eml2.eml", FileType = "eml", ControlNumber = "CUSTOM-CHILD" },
        };

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 2 },
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
            SourceRecords = records,
        };

        var plan = EmailThreadPlan.GetOrCreate(request);
        var node1 = plan.GetNode(1);
        var node2 = plan.GetNode(2);

        Assert.Equal("CUSTOM-ROOT", node1.ThreadDocId);
        Assert.Equal("CUSTOM-ROOT", node2.ThreadDocId);
        Assert.Equal(1, node2.ParentIndex);
        Assert.Equal("CUSTOM-ROOT", node2.ThreadParentDocId);
    }

    [Fact]
    public void GetNode_AllSourceRecordsEml_WithoutOutputCount_UsesSourceRecordsCount()
    {
        var records = new List<Zipper.SourceInput.SourceRecord>
        {
            new() { RelativePath = "eml1.eml", FileType = "eml" },
            new() { RelativePath = "eml2.eml", FileType = "eml" },
            new() { RelativePath = "eml3.eml", FileType = "eml" },
        };

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml" }, // FileCount defaults to 0
            Metadata = new MetadataConfig { WithThreading = true, Seed = 42 },
            SourceRecords = records,
        };

        var plan = EmailThreadPlan.GetOrCreate(request);
        var node3 = plan.GetNode(3);

        Assert.NotNull(node3);
        Assert.NotNull(node3.MessageId);
    }
}
