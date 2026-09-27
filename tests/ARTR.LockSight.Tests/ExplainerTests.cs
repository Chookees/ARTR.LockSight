using ARTR.LockSight.Explain;

namespace ARTR.LockSight.Tests;

public sealed class ExplainerTests
{
    [Fact]
    public void Explain_mentions_nu1004_and_next_steps()
    {
        using var writer = new StringWriter();
        Nu1004Explainer.WriteExplanation("nu1004", writer);
        string text = writer.ToString();

        Assert.Contains("NU1004", text, StringComparison.Ordinal);
        Assert.Contains("RestoreLockedMode", text, StringComparison.Ordinal);
        Assert.Contains("artr-locksight drift", text, StringComparison.Ordinal);
        Assert.Contains("Dependabot", text, StringComparison.Ordinal);
    }
}
