using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

[TestFixture]
public class FileExtensionUtilsTests
{
    [TestCase(".md")]
    [TestCase(".txt")]
    [TestCase(".7z")]
    [TestCase(".tar.gz")]
    [TestCase(".d.ts")]
    [TestCase(".min.js")]
    [TestCase(".c-header")]
    [TestCase(".a_b")]
    public void IsWellFormedFileExtension_AcceptsWellFormedExtensions(string extension)
    {
        FileExtensionUtils.IsWellFormedFileExtension(extension).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase(".")]
    [TestCase("md")]
    [TestCase(". ")]
    [TestCase(".a b")]
    [TestCase(".a/b")]
    [TestCase(".a\\b")]
    [TestCase("..")]
    [TestCase(".a.")]
    [TestCase(".a..b")]
    [TestCase(".<x>")]
    public void IsWellFormedFileExtension_RejectsMalformedExtensions(string extension)
    {
        FileExtensionUtils.IsWellFormedFileExtension(extension).Should().BeFalse();
    }
}
