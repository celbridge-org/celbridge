using Celbridge.Reports;

namespace Celbridge.Tests.Reports;

/// <summary>
/// Tests that a report code holds only what can survive a round trip through the report file and a
/// help topic lookup, and that the absence of a code is a value rather than a malformed one.
/// </summary>
[TestFixture]
public class ReportCodeTests
{
    [Test]
    public void ACodeRoundTripsThroughItsStringForm()
    {
        var code = new ReportCode("CEL_PROJECT_001");

        code.ToString().Should().Be("CEL_PROJECT_001");
        code.IsEmpty.Should().BeFalse();
        code.Should().Be(new ReportCode("CEL_PROJECT_001"));
    }

    [Test]
    public void TheDefaultValueIsEmpty()
    {
        var code = default(ReportCode);

        code.IsEmpty.Should().BeTrue();
        code.Should().Be(ReportCode.Empty);
        code.ToString().Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("CEL PROJECT 001")]
    [TestCase("CEL_PROJECT_001\n")]
    public void AValueThatCannotBeACodeIsRejected(string value)
    {
        ReportCode.IsValid(value).Should().BeFalse();
        ReportCode.TryParse(value, out var parsed).Should().BeFalse();
        parsed.Should().Be(ReportCode.Empty);

        var construct = () => new ReportCode(value);
        construct.Should().Throw<ArgumentException>();
    }

    [Test]
    public void APackageNamespacedCodeIsValid()
    {
        // The host format is a convention on top of the type. A contribution's code is namespaced by
        // its package and never matches it, so the type must not hold codes to it.
        var code = new ReportCode("acme.tiles.broken-tileset");

        code.ToString().Should().Be("acme.tiles.broken-tileset");
    }
}
