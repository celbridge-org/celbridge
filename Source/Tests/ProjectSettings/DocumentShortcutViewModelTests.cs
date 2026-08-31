using Celbridge.ProjectSettings.ViewModels;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.ProjectSettings;

[TestFixture]
public class DocumentShortcutViewModelTests
{
    private static DocumentShortcutViewModel CreateShortcut(
        string resource,
        string icon = "",
        bool resourceExists = true,
        WorkspaceArea area = WorkspaceArea.Main)
    {
        return new DocumentShortcutViewModel(new IconService(), fileResource => resourceExists)
        {
            Resource = resource,
            Icon = icon,
            Area = area
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

    /// <summary>
    /// A picker with no selection reports -1. The area must survive that rather than reading out of range
    /// or resetting to the default.
    /// </summary>
    [Test]
    public void SelectedAreaIndex_SetToNoSelection_LeavesTheAreaUnchanged()
    {
        var shortcut = CreateShortcut("readme.md", area: WorkspaceArea.Bottom);

        shortcut.SelectedAreaIndex = -1;

        shortcut.Area.Should().Be(WorkspaceArea.Bottom);
        shortcut.SelectedAreaIndex.Should().Be(1);
        shortcut.ToDocumentShortcut().Area.Should().Be(WorkspaceArea.Bottom);
    }

    [Test]
    public void SelectedAreaIndex_SelectsTheMatchingArea()
    {
        var shortcut = CreateShortcut("readme.md");

        shortcut.SelectedAreaIndex = 2;

        shortcut.Area.Should().Be(WorkspaceArea.Side);
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
