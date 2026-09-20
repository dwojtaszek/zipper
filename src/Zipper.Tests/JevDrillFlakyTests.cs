using Xunit;

namespace Zipper.Tests;

// Jev gate fire drill (temporary, drill branch only — never merged):
// a deliberately intermittent test so a real CI run fails, the runner's
// CI-failure triage classifies it, and the auto-rerun path can be exercised
// end-to-end against a live failed run.
public class JevDrillFlakyTests
{
    [Fact]
    public void Drill_IntermittentAssertion_SometimesFails()
    {
        if (Random.Shared.Next(2) == 0)
        {
            Assert.Fail("Drill flake: intermittent by design (Jev gate fire drill, not a product bug).");
        }
    }
}
