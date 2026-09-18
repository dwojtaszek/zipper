using Xunit;

public class FixtureTests
{
    [Fact]
    public void Full()
    {
        var result = Sample.Add(1, 2);
        Assert.Equal(3, result);
        Assert.True(result > 0);
    }

    [Fact]
    public void Partial()
    {
        var result = Sample.Add(1, 2);
        Assert.Equal(3, result);
    }

    [Fact]
    public void None()
    {
        Unrelated.Touch();
    }

    [Fact]
    public void Ambiguous()
    {
        var result = Sample.Add(1, 2);
        Assert.Equal(3, result);
    }

    [Fact]
    public void MockedOnly()
    {
        var substitute = new SampleSubstitute();
        substitute.Add(1, 2);
        substitute.Received();
    }

    [Fact]
    public void ExecutionOnly()
    {
        Sample.Add(1, 2);
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(2, 3, 5)]
    public void TheoryFull(int a, int b, int expected)
    {
        var result = Sample.Add(a, b);
        Assert.Equal(expected, result);
    }
}

public static class Sample
{
    public static int Add(int a, int b) => a + b;
}

public class DupTests
{
    [Fact]
    public void Dup()
    {
        Assert.True(true);
    }
}
