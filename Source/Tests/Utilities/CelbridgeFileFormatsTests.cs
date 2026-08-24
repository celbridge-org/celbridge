using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Covers which file names count as Celbridge's own formats. One list drives the resource policy floor
/// and the sidecar refusal, so a name landing on the wrong side of it changes both.
/// </summary>
[TestFixture]
public class CelbridgeFileFormatsTests
{
    [TestCase("Acme.celbridge")]
    [TestCase("package.toml")]
    [TestCase("code.editor.toml")]
    [TestCase("markdown.editor.toml")]
    public void IsCelbridgeFormat_TheApplicationsOwnFormats_AreRecognized(string fileName)
    {
        CelbridgeFileFormats.IsCelbridgeFormat(fileName).Should().BeTrue();
    }

    [TestCase("settings.toml")]
    [TestCase("notes.md")]
    [TestCase("celbridge.txt")]
    public void IsCelbridgeFormat_OrdinaryContent_IsNot(string fileName)
    {
        CelbridgeFileFormats.IsCelbridgeFormat(fileName).Should().BeFalse();
    }

    [Test]
    public void IsCelbridgeFormat_ABareEditorToml_IsNotAManifest()
    {
        // A manifest reference must end with ".editor.toml", which a file named "editor.toml" does not,
        // so it is an ordinary TOML file. The old policy rule matched this name and nothing else.
        CelbridgeFileFormats.IsCelbridgeFormat("editor.toml").Should().BeFalse();
    }

    [Test]
    public void IsCelbridgeFormat_AnEmptyName_IsNot()
    {
        CelbridgeFileFormats.IsCelbridgeFormat(string.Empty).Should().BeFalse();
    }
}
