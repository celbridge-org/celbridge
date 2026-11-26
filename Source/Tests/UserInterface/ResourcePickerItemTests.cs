using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers how the resource picker draws an item: its name, the folder a search result shows and the accessible
/// name. Also covers the dimming and tooltip of read-only and hidden items.
/// </summary>
[TestFixture]
public class ResourcePickerItemTests
{
    private static readonly IconDefinition Icon =
        new("a", "#FFFFFF", "Segoe Fluent Icons", "16");

    private static readonly ResourcePickerItemState Writable =
        new(IsHidden: false, WritableState.Writable, ReadOnlyMessage: string.Empty);

    [Test]
    public void AnItemInAFolderListing_ShowsItsName_WithNoFolder()
    {
        var item = new ResourcePickerItem(new ResourceKey("docs/photo.png"), false, Icon, Writable, showsFolder: false);

        item.Name.Should().Be("photo.png");
        item.FolderText.Should().BeEmpty();
        item.FolderTextVisibility.Should().Be(Visibility.Collapsed);
        item.ToString().Should().Be("photo.png");
        item.PathLower.Should().Be("docs/photo.png");
    }

    [Test]
    public void ASearchResult_ShowsItsFolder_AndIsNamedByItsPath()
    {
        var item = new ResourcePickerItem(new ResourceKey("docs/Photo.png"), false, Icon, Writable, showsFolder: true);

        item.Name.Should().Be("Photo.png");
        item.FolderText.Should().Be("docs");
        item.FolderTextVisibility.Should().Be(Visibility.Visible);
        item.ToString().Should().Be("docs/Photo.png");
        item.PathLower.Should().Be("docs/photo.png");
    }

    [Test]
    public void AWritableItem_IsNotDimmed_AndHasNoTooltip()
    {
        var item = new ResourcePickerItem(new ResourceKey("notes.txt"), false, Icon, Writable, showsFolder: false);

        item.IsReadOnly.Should().BeFalse();
        item.IsDimmed.Should().BeFalse();
        item.TooltipText.Should().BeNull();
        item.ReadOnlyMessage.Should().BeEmpty();
    }

    [Test]
    public void AReadOnlyItem_IsDimmed_WithTheReadOnlyMessage()
    {
        var state = new ResourcePickerItemState(
            IsHidden: false,
            WritableState.ReadOnlyAttribute,
            ReadOnlyMessage: "File is read-only on disk.");

        var item = new ResourcePickerItem(new ResourceKey("notes.txt"), false, Icon, state, showsFolder: false);

        item.IsReadOnly.Should().BeTrue();
        item.IsDimmed.Should().BeTrue();
        item.TooltipText.Should().Be("File is read-only on disk.");
    }

    [Test]
    public void AHiddenItem_IsDimmed_WithNoTooltip()
    {
        var state = new ResourcePickerItemState(
            IsHidden: true,
            WritableState.Writable,
            ReadOnlyMessage: string.Empty);

        var item = new ResourcePickerItem(new ResourceKey("drafts"), true, Icon, state, showsFolder: false);

        item.IsHidden.Should().BeTrue();
        item.IsDimmed.Should().BeTrue();
        item.TooltipText.Should().BeNull();
    }
}
