namespace Celbridge.Tests.Documents;

[TestFixture]
public class EditorIdTests
{
    [Test]
    public void Empty_IsEmpty_ReturnsTrue()
    {
        EditorId.Empty.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Empty_ToString_ReturnsEmptyString()
    {
        EditorId.Empty.ToString().Should().BeEmpty();
    }

    [Test]
    public void Default_IsEmpty_ReturnsTrue()
    {
        EditorId defaultValue = default;

        defaultValue.IsEmpty.Should().BeTrue();
    }

    [TestCase("celbridge.code-editor")]
    [TestCase("markdown.preview")]
    [TestCase("a.b")]
    [TestCase("scope.name-with-multiple-hyphens")]
    [TestCase("scope123.editor456")]
    [TestCase("dot-free-id")]
    public void IsValid_AcceptsWellFormedIds(string input)
    {
        EditorId.IsValid(input).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("Uppercase.NotAllowed")]
    [TestCase("invalid.char$")]
    [TestCase("space is.bad")]
    [TestCase("under_score.bad")]
    public void IsValid_RejectsInvalidIds(string input)
    {
        EditorId.IsValid(input).Should().BeFalse();
    }

    [Test]
    public void Create_ComposesPackageNameAndContributionId()
    {
        var editorId = EditorId.Create("acme", "notepad");

        editorId.ToString().Should().Be("acme.notepad");
    }

    [Test]
    public void Create_ThrowsOnEmptyPart()
    {
        var missingPackage = () => EditorId.Create(string.Empty, "notepad");
        var missingContribution = () => EditorId.Create("acme", string.Empty);

        missingPackage.Should().Throw<ArgumentException>();
        missingContribution.Should().Throw<ArgumentException>();
    }

    [Test]
    public void IsValid_RejectsNullInput()
    {
        EditorId.IsValid(null!).Should().BeFalse();
    }

    [Test]
    public void Constructor_ThrowsOnInvalidInput()
    {
        var invocation = () => new EditorId("INVALID");

        invocation.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Constructor_ThrowsOnEmptyInput()
    {
        var invocation = () => new EditorId(string.Empty);

        invocation.Should().Throw<ArgumentException>();
    }

    [Test]
    public void TryParse_SucceedsForValidInput()
    {
        var parsed = EditorId.TryParse("celbridge.code-editor", out var editorId);

        parsed.Should().BeTrue();
        editorId.ToString().Should().Be("celbridge.code-editor");
        editorId.IsEmpty.Should().BeFalse();
    }

    [Test]
    public void TryParse_FailsForInvalidInputAndReturnsEmpty()
    {
        var parsed = EditorId.TryParse("NOT-VALID", out var editorId);

        parsed.Should().BeFalse();
        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void TryParse_FailsForNullAndReturnsEmpty()
    {
        var parsed = EditorId.TryParse(null, out var editorId);

        parsed.Should().BeFalse();
        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void TryParse_FailsForEmptyAndReturnsEmpty()
    {
        var parsed = EditorId.TryParse(string.Empty, out var editorId);

        parsed.Should().BeFalse();
        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Equality_MatchesIdsWithSameValue()
    {
        var left = new EditorId("celbridge.code-editor");
        var right = new EditorId("celbridge.code-editor");

        (left == right).Should().BeTrue();
        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Test]
    public void Equality_DistinguishesDifferentIds()
    {
        var left = new EditorId("celbridge.code-editor");
        var right = new EditorId("celbridge.markdown-preview");

        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Test]
    public void Equality_EmptyEqualsDefault()
    {
        EditorId empty = EditorId.Empty;
        EditorId defaultValue = default;

        (empty == defaultValue).Should().BeTrue();
    }
}
