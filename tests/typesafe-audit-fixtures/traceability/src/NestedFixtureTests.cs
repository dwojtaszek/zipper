public class NestedFixtureTests
{
    public class Inner
    {
        [Xunit.Fact]
        public void InnerCheck()
        {
            Xunit.Assert.NotEmpty(new[] { 1 });
        }
    }
}

public class Other
{
    public static void Touch()
    {
    }
}

public class DupTests
{
    [Xunit.Fact]
    public void Dup()
    {
        Xunit.Assert.True(false);
    }
}
