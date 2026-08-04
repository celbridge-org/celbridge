using Celbridge.Packages;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class PackageNameTests
{
    [TestCase("a", Description = "single character")]
    [TestCase("simple", Description = "single word")]
    [TestCase("my-widget", Description = "hyphen separator")]
    [TestCase("my-cool-package", Description = "multiple hyphen separators")]
    [TestCase("package123", Description = "trailing digits")]
    [TestCase("123package", Description = "leading digits")]
    [TestCase("a1b2c3", Description = "mixed letters and digits")]
    public void IsValid_WellFormedNames_Accepted(string name)
    {
        PackageName.IsValid(name).Should().BeTrue();
    }

    [TestCase("", Description = "empty")]
    [TestCase("My-Widget", Description = "uppercase rejected")]
    [TestCase("-widget", Description = "leading hyphen rejected")]
    [TestCase("widget-", Description = "trailing hyphen rejected")]
    [TestCase("my--widget", Description = "consecutive hyphens rejected")]
    [TestCase("my widget", Description = "whitespace rejected")]
    [TestCase("my_widget", Description = "underscore rejected")]
    [TestCase("my.widget", Description = "dot rejected")]
    [TestCase("my/widget", Description = "slash rejected")]
    public void IsValid_MalformedNames_Rejected(string name)
    {
        PackageName.IsValid(name).Should().BeFalse();
    }

    [Test]
    public void IsValid_HomographLookalike_Rejected()
    {
        // Cyrillic 'о' (U+043E) masquerading as ASCII 'o'.
        PackageName.IsValid("my-widgоt").Should().BeFalse();
    }

    [Test]
    public void IsValid_NameAtMaxLength_Accepted()
    {
        var maxName = new string('a', PackageConstants.MaxNameLength);

        PackageName.IsValid(maxName).Should().BeTrue();
    }

    [Test]
    public void IsValid_NameOverMaxLength_Rejected()
    {
        var overLongName = new string('a', PackageConstants.MaxNameLength + 1);

        PackageName.IsValid(overLongName).Should().BeFalse();
    }

    // One grammar for every origin, so a bundled name is an ordinary name carrying the reserved prefix.
    [TestCase("celbridge-notes", Description = "bundled name")]
    [TestCase("celbridge.notes", Description = "dotted name rejected for every origin")]
    [TestCase("a.b", Description = "minimal dotted")]
    [TestCase("Celbridge-Notes", Description = "uppercase rejected")]
    [TestCase("has_underscore", Description = "underscore rejected")]
    public void IsValid_DottedAndMalformedNames_Rejected(string name)
    {
        PackageName.IsValid(name).Should().Be(name == "celbridge-notes");
    }
}
