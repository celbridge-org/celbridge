using Celbridge.ProjectSettings.ViewModels;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.ProjectSettings;

[TestFixture]
public class DocumentShortcutViewModelTests
{
    private static DocumentShortcutViewModel CreateShortcut(
        string resource,
        string icon = "",
        bool resourceExists = true)
    {
        return new DocumentShortcutViewModel(new IconService(), fileResource => resourceExists)
        {
            Resource = resource,
            Icon = icon
        };
    }

    [Test]
    public void DisplayName_IsTheFileNameOfTheResource()
    {
        var shortcut = CreateShortcut("docs/guide.md");

        shortcut.DisplayName.Should().Be("guide.md");
    }

    /// <summary>
    /// Text that is not a resource key cannot be reduced to a file name, so the header shows what the user
    /// typed rather than hiding the mistake behind a placeholder.
    /// </summary>
    [Test]
    public void DisplayName_ForAnInvalidResource_IsTheTextAsTyped()
    {
        var shortcut = CreateShortcut("docs//guide.md");

        shortcut.IsResourceInvalid.Should().BeTrue();
        shortcut.DisplayName.Should().Be("docs//guide.md");
    }

    [Test]
    public void IconName_WithNoIconNamed_IsTheDefaultDocumentIcon()
    {
        var shortcut = CreateShortcut("readme.md");

        shortcut.IconName.Should().Be("bs-file-earmark");
        shortcut.IsIconUnknown.Should().BeFalse();
    }

    [Test]
    public void IconName_WithAnUnknownIconNamed_KeepsTheNameAndReportsIt()
    {
        // The rail still draws the button, the icon service resolving the name to a fallback glyph.
        var shortcut = CreateShortcut("readme.md", "bs-not-a-real-icon");

        shortcut.IconName.Should().Be("bs-not-a-real-icon");
        shortcut.IsIconUnknown.Should().BeTrue();
    }

    [Test]
    public void IsResourceMissing_IsTrueWhenTheProjectHasNoSuchResource()
    {
        var missing = CreateShortcut("readme.md", resourceExists: false);
        missing.IsResourceMissing.Should().BeTrue();

        var present = CreateShortcut("readme.md");
        present.IsResourceMissing.Should().BeFalse();
    }

    [Test]
    public void ToDocumentShortcut_TrimsTheEditedFields()
    {
        var shortcut = CreateShortcut("  readme.md  ", "  bs-book  ");

        var documentShortcut = shortcut.ToDocumentShortcut();

        documentShortcut.Resource.Should().Be("readme.md");
        documentShortcut.Icon.Should().Be("bs-book");
    }
}
