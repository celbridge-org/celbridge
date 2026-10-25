namespace Celbridge.Tests.Patterns;

[TestFixture]
public class BraceExpanderTests
{
    private static IReadOnlyList<string> Expand(string glob)
    {
        var expandResult = BraceExpander.Expand(glob);
        expandResult.IsSuccess.Should().BeTrue(expandResult.FirstErrorMessage);

        return expandResult.Value;
    }

    [Test]
    public void GlobWithoutBraces_IsUnchanged()
    {
        Expand("src/*.ts").Should().Equal("src/*.ts");
    }

    [Test]
    public void Braces_ExpandInOrder()
    {
        Expand("*.{ts,tsx,js}").Should().Equal("*.ts", "*.tsx", "*.js");
    }

    [Test]
    public void SeveralGroups_ExpandToEveryCombination()
    {
        Expand("{src,lib}/*.{ts,js}").Should().Equal("src/*.ts", "src/*.js", "lib/*.ts", "lib/*.js");
    }

    [Test]
    public void NestedGroups_Expand()
    {
        Expand("{a,b{c,d}}.txt").Should().Equal("a.txt", "bc.txt", "bd.txt");
    }

    [Test]
    public void AnEmptyAlternative_ExpandsToNothing()
    {
        Expand("app{,.min}.js").Should().Equal("app.js", "app.min.js");
    }

    [Test]
    public void EscapedBracesAndBracesInAClass_StayAsWritten()
    {
        Expand(@"\{a,b\}").Should().Equal(@"\{a,b\}");
        Expand("[{,]x").Should().Equal("[{,]x");
    }

    [TestCase("*.{ts,tsx", "has no closing '}'")]
    [TestCase("*.ts}", "has no opening '{'")]
    public void UnbalancedBraces_AreRefused(string glob, string expectedMessage)
    {
        var expandResult = BraceExpander.Expand(glob);

        expandResult.IsFailure.Should().BeTrue();
        expandResult.FirstErrorMessage.Should().Contain(expectedMessage);
    }

    [Test]
    public void ExpansionPastTheCap_IsRefused()
    {
        // Seven groups of two alternatives make 128 patterns.
        var expandResult = BraceExpander.Expand("{a,b}{a,b}{a,b}{a,b}{a,b}{a,b}{a,b}");

        expandResult.IsFailure.Should().BeTrue();
        expandResult.FirstErrorMessage.Should().Contain($"more than {BraceExpander.MaxPatterns} patterns");
    }

    [Test]
    public void ExpansionAtTheCap_IsAccepted()
    {
        // Two groups of ten alternatives make 100 patterns.
        var glob = "{0,1,2,3,4,5,6,7,8,9}{0,1,2,3,4,5,6,7,8,9}";

        Expand(glob).Should().HaveCount(BraceExpander.MaxPatterns);
    }
}
