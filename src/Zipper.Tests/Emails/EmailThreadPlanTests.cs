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

        // To ensure we evaluate complete threads without partial thread cutoffs at the end of the range,
        // fetch the remaining nodes belonging to the last root thread.
        long lastRoot = nodes.Last().RootIndex;
        long next = 31;
        while (plan.GetNode(next).RootIndex == lastRoot)
        {
            nodes.Add(plan.GetNode(next));
            next++;
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
}
