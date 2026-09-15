namespace Celbridge.Tests.Core;

/// <summary>
/// Covers the one version format: what parses, the forms that are rejected, how versions order, and how an
/// optional version field reads.
/// </summary>
[TestFixture]
public class SemanticVersionTests
{
    [TestCase("0.0.0", 0, 0, 0)]
    [TestCase("1.2.3", 1, 2, 3)]
    [TestCase("10.20.30", 10, 20, 30)]
    public void TryParse_ThreePartVersion_ReadsEachPart(string text, int major, int minor, int patch)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();

        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
    }

    [TestCase("")]
    [TestCase("1")]
    [TestCase("1.2")]
    [TestCase("1.2.3.4")]
    [TestCase("v1.2.3")]
    [TestCase("1.2.3-beta")]
    [TestCase("1.2.3+build")]
    [TestCase(" 1.2.3")]
    [TestCase("1.2.3 ")]
    [TestCase("01.2.3")]
    [TestCase("1.02.3")]
    [TestCase("1.2.03")]
    [TestCase("+1.2.3")]
    [TestCase("-1.2.3")]
    [TestCase("1.x.3")]
    public void TryParse_AnythingButAThreePartVersion_IsRejected(string text)
    {
        SemanticVersion.TryParse(text, out _).Should().BeFalse();
    }

    /// <summary>
    /// Each part compares as a number, so a two-digit part orders after a one-digit part rather than
    /// between its digits.
    /// </summary>
    [Test]
    public void CompareTo_OrdersByMajorThenMinorThenPatch()
    {
        var versions = new[]
        {
            new SemanticVersion(2, 0, 0),
            new SemanticVersion(1, 10, 0),
            new SemanticVersion(1, 9, 1),
            new SemanticVersion(1, 9, 0),
        };

        versions.Order().Should().Equal(
            new SemanticVersion(1, 9, 0),
            new SemanticVersion(1, 9, 1),
            new SemanticVersion(1, 10, 0),
            new SemanticVersion(2, 0, 0));
    }

    [Test]
    public void Operators_AgreeWithCompareTo()
    {
        var older = new SemanticVersion(1, 9, 0);
        var newer = new SemanticVersion(1, 10, 0);

        (older < newer).Should().BeTrue();
        (newer > older).Should().BeTrue();
        (older <= new SemanticVersion(1, 9, 0)).Should().BeTrue();
        (newer >= older).Should().BeTrue();
        (older == new SemanticVersion(1, 9, 0)).Should().BeTrue();
        (older != newer).Should().BeTrue();
    }

    [Test]
    public void ToString_FormatsTheThreeParts()
    {
        new SemanticVersion(1, 20, 3).ToString().Should().Be("1.20.3");
        SemanticVersion.Default.ToString().Should().Be("1.0.0");
    }

    [Test]
    public void ParseOptional_MissingOrEmptyValue_IsTheDefault()
    {
        SemanticVersion.ParseOptional(null).Value.Should().Be(SemanticVersion.Default);
        SemanticVersion.ParseOptional(string.Empty).Value.Should().Be(SemanticVersion.Default);
    }

    [Test]
    public void ParseOptional_ValidValue_IsThatVersion()
    {
        var result = SemanticVersion.ParseOptional("2.1.0");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new SemanticVersion(2, 1, 0));
    }

    [Test]
    public void ParseOptional_MalformedValue_FailsWithTheStandardMessage()
    {
        var result = SemanticVersion.ParseOptional("1.2");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Be("'1.2' is not a three-part version such as 1.0.0.");
    }
}
