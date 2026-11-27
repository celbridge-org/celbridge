using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers how the resource picker draws an item: the item's name, a search result's path and mark, and the accessible
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
    public void AnItemInAFolderListing_ShowsItsName_WithNoPath()
    {
        var item = new ResourcePickerItem(new ResourceKey("docs/photo.png"), false, Icon, Writable, showsPath: false);

        item.Name.Should().Be("photo.png");
        item.MarkedPath.Before.Should().BeEmpty();
        item.PathVisibility.Should().Be(Visibility.Collapsed);
        item.ToString().Should().Be("photo.png");
        item.PathLower.Should().Be("docs/photo.png");
    }

    [Test]
    public void ASearchResult_ShowsItsFullPath_AndIsNamedByItsPath()
    {
        var item = new ResourcePickerItem(new ResourceKey("docs/Photo.png"), false, Icon, Writable, showsPath: true);

        item.Name.Should().Be("Photo.png");
        item.MarkedPath.Before.Should().Be("docs/Photo.png");
        item.MarkedPath.Mark.Should().BeEmpty();
        item.MarkedPath.After.Should().BeEmpty();
        item.PathVisibility.Should().Be(Visibility.Visible);
        item.TooltipText.Should().Be("docs/Photo.png");
        item.ToString().Should().Be("docs/Photo.png");
        item.PathLower.Should().Be("docs/photo.png");
    }

    [Test]
    public void ASearchResultInTheProjectFolder_ShowsItsNameAsItsPath()
    {
        var item = new ResourcePickerItem(new ResourceKey("readme.md"), false, Icon, Writable, showsPath: true);

        item.MarkedPath.Before.Should().Be("readme.md");
        item.PathVisibility.Should().Be(Visibility.Visible);
    }

    [Test]
    public void AMarkedSearchResult_SplitsItsPathAroundTheMark()
    {
        var item = new ResourcePickerItem(new ResourceKey("docs/Photo.png"), false, Icon, Writable, showsPath: true);

        var marked = item.WithPathMark(5, 5);

        marked.MarkedPath.Before.Should().Be("docs/");
        marked.MarkedPath.Mark.Should().Be("Photo");
        marked.MarkedPath.After.Should().Be(".png");
        marked.Resource.Should().Be(item.Resource);
        marked.ToString().Should().Be("docs/Photo.png");
        item.MarkedPath.Mark.Should().BeEmpty();
    }

    [Test]
    public void AReadOnlySearchResult_ShowsItsPathAndTheReadOnlyMessage()
    {
        var state = new ResourcePickerItemState(
            IsHidden: false,
            WritableState.ReadOnlyAttribute,
            ReadOnlyMessage: "File is read-only on disk.");

        var item = new ResourcePickerItem(new ResourceKey("docs/notes.txt"), false, Icon, state, showsPath: true);

        item.TooltipText.Should().Be("docs/notes.txt\nFile is read-only on disk.");
    }

    [Test]
    public void AWritableItem_IsNotDimmed_AndHasNoTooltip()
    {
        var item = new ResourcePickerItem(new ResourceKey("notes.txt"), false, Icon, Writable, showsPath: false);

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

        var item = new ResourcePickerItem(new ResourceKey("notes.txt"), false, Icon, state, showsPath: false);

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

        var item = new ResourcePickerItem(new ResourceKey("drafts"), true, Icon, state, showsPath: false);

        item.IsHidden.Should().BeTrue();
        item.IsDimmed.Should().BeTrue();
        item.TooltipText.Should().BeNull();
    }
}
