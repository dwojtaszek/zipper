using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class FileTypePlanTests
{
    private static FileTypePlan Plan(string spec, long count)
    {
        Assert.True(FileTypeRatioParser.TryParse(spec, out var ratios, out var error), error);
        return new FileTypePlan(ratios, count);
    }

    [Fact]
    public void GetTypeCount_EvenSplit_AllocatesExactly()
    {
        var plan = Plan("pdf:50,eml:50", 100);

        Assert.Equal(50, plan.GetTypeCount("pdf"));
        Assert.Equal(50, plan.GetTypeCount("eml"));
    }

    [Fact]
    public void GetTypeCount_UnevenRatios_AllocatesByLargestRemainder()
    {
        // 10 * 1/3 = 3.33 each; largest-remainder gives the first declared type the extra file.
        var plan = Plan("pdf:1,eml:1,tiff:1", 10);

        Assert.Equal(4, plan.GetTypeCount("pdf"));
        Assert.Equal(3, plan.GetTypeCount("eml"));
        Assert.Equal(3, plan.GetTypeCount("tiff"));
    }

    [Theory]
    [InlineData("pdf:1,eml:1,tiff:1,jpg:1,docx:1,xlsx:1", 1)]
    [InlineData("pdf:1,eml:1,tiff:1,jpg:1,docx:1,xlsx:1", 7)]
    [InlineData("pdf:7,eml:13,tiff:29", 999)]
    [InlineData("pdf:1,eml:2", 1000000)]
    [InlineData("tiff:99,pdf:1", 2)]
    public void GetTypeCount_AnyInput_NeverOverOrUnderproduces(string spec, long count)
    {
        var plan = Plan(spec, count);

        long total = 0;
        foreach (var type in plan.Types)
        {
            total += plan.GetTypeCount(type);
        }

        Assert.Equal(count, total);
    }

    [Fact]
    public void GetTypeCount_ZeroWeightTypeAfterRounding_StillExact()
    {
        // eml rounds down to zero files; the whole count lands on pdf.
        var plan = Plan("pdf:9999,eml:1", 100);

        Assert.Equal(100, plan.GetTypeCount("pdf"));
        Assert.Equal(0, plan.GetTypeCount("eml"));
    }

    [Fact]
    public void GetFileType_ContiguousDeclaredOrder_AssignedByIndexRange()
    {
        var plan = Plan("pdf:1,eml:1,tiff:1", 10);

        // pdf gets 4 (indexes 1-4), eml 3 (5-7), tiff 3 (8-10).
        for (long i = 1; i <= 4; i++)
        {
            Assert.Equal("pdf", plan.GetFileType(i));
        }

        for (long i = 5; i <= 7; i++)
        {
            Assert.Equal("eml", plan.GetFileType(i));
        }

        for (long i = 8; i <= 10; i++)
        {
            Assert.Equal("tiff", plan.GetFileType(i));
        }
    }

    [Fact]
    public void GetFileType_OutOfRange_Throws()
    {
        var plan = Plan("pdf:1,eml:1", 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.GetFileType(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.GetFileType(11));
    }

    [Fact]
    public void Plan_SameInputs_IsDeterministic()
    {
        var a = Plan("pdf:7,eml:13,tiff:29", 1000);
        var b = Plan("pdf:7,eml:13,tiff:29", 1000);

        for (long i = 1; i <= 1000; i++)
        {
            Assert.Equal(a.GetFileType(i), b.GetFileType(i));
        }
    }

    [Fact]
    public void Plan_SingleEntry_AllIndexesGetThatType()
    {
        var plan = Plan("eml:5", 42);

        Assert.Equal(42, plan.GetTypeCount("eml"));
        Assert.Equal("eml", plan.GetFileType(1));
        Assert.Equal("eml", plan.GetFileType(42));
    }

    [Fact]
    public void ContainsType_IsCaseInsensitive()
    {
        var plan = Plan("pdf:1,eml:1", 10);

        Assert.True(plan.ContainsType("PDF"));
        Assert.True(plan.ContainsType("eml"));
        Assert.False(plan.ContainsType("tiff"));
    }

    [Fact]
    public void Constructor_EmptyRatios_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FileTypePlan(new List<FileTypeRatio>(), 10));
    }

    [Fact]
    public void Constructor_NonPositiveCount_Throws()
    {
        Assert.True(FileTypeRatioParser.TryParse("pdf:1", out var ratios, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileTypePlan(ratios, 0));
    }
}
