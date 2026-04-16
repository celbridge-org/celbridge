namespace Celbridge.Tests.Documents;

[TestFixture]
public class DocumentEditorIdTests
{
    [Test]
    public void Empty_IsEmpty_ReturnsTrue()
    {
        DocumentEditorId.Empty.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Empty_ToString_ReturnsEmptyString()
    {
        DocumentEditorId.Empty.ToString().Should().BeEmpty();
    }

    [Test]
    public void Default_IsEmpty_ReturnsTrue()
    {
        // Struct default must be equivalent to Empty to avoid surprising the caller.
        DocumentEditorId defaultValue = default;

        defaultValue.IsEmpty.Should().BeTrue();
    }

    [TestCase("celbridge.code-editor")]
    [TestCase("markdown.preview")]
    [TestCase("a.b")]
    [TestCase("scope.name-with-multiple-hyphens")]
    [TestCase("scope123.editor456")]
    public void IsValid_AcceptsWellFormedIds(string input)
    {
        DocumentEditorId.IsValid(input).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("no-dot")]
    [TestCase("Uppercase.NotAllowed")]
    [TestCase("invalid.char$")]
    [TestCase("space is.bad")]
    [TestCase("under_score.bad")]
    public void IsValid_RejectsInvalidIds(string input)
    {
        DocumentEditorId.IsValid(input).Should().BeFalse();
    }

    [Test]
    public void IsValid_RejectsNullInput()
    {
        DocumentEditorId.IsValid(null!).Should().BeFalse();
    }

    [Test]
    public void Constructor_ThrowsOnInvalidInput()
    {
        var invocation = () => new DocumentEditorId("INVALID");

        invocation.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Constructor_ThrowsOnEmptyInput()
    {
        var invocation = () => new DocumentEditorId(string.Empty);

        invocation.Should().Throw<ArgumentException>();
    }

    [Test]
    public void TryParse_SucceedsForValidInput()
    {
        var parsed = DocumentEditorId.TryParse("celbridge.code-editor", out var editorId);

        parsed.Should().BeTrue();
        editorId.ToString().Should().Be("celbridge.code-editor");
        editorId.IsEmpty.Should().BeFalse();
    }

    [Test]
    public void TryParse_FailsForInvalidInputAndReturnsEmpty()
    {
        var parsed = DocumentEditorId.TryParse("NOT-VALID", out var editorId);

        parsed.Should().BeFalse();
        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void TryParse_FailsForNullAndReturnsEmpty()
    {
        var parsed = DocumentEditorId.TryParse(null, out var editorId);

        parsed.Should().BeFalse();
        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void TryParse_FailsForEmptyAndReturnsEmpty()
    {
        var parsed = DocumentEditorId.TryParse(string.Empty, out var editorId);

        parsed.Should().BeFalse();
        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Equality_MatchesIdsWithSameValue()
    {
        var left = new DocumentEditorId("celbridge.code-editor");
        var right = new DocumentEditorId("celbridge.code-editor");

        (left == right).Should().BeTrue();
        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Test]
    public void Equality_DistinguishesDifferentIds()
    {
        var left = new DocumentEditorId("celbridge.code-editor");
        var right = new DocumentEditorId("celbridge.markdown-preview");

        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Test]
    public void Equality_EmptyEqualsDefault()
    {
        DocumentEditorId empty = DocumentEditorId.Empty;
        DocumentEditorId defaultValue = default;

        (empty == defaultValue).Should().BeTrue();
    }
}
