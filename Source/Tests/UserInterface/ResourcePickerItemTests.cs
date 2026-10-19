using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests for the read-only state surface that the resource picker uses to render
/// dimming, tooltip, and assistive-tech labelling.
/// </summary>
[TestFixture]
public class ResourcePickerItemTests
{
    private static readonly IconDefinition Icon =
        new("a", "#FFFFFF", "Segoe Fluent Icons", "16");

    [Test]
    public void Writable_ResourceProducesWritableStateNoTooltipNoHelpText()
    {
        var writableState = WritableState.Writable;

        var item = new ResourcePickerItem(new ResourceKey("docs/photo.png"), Icon, writableState);

        item.IsReadOnly.Should().BeFalse();
        item.IsWritable.Should().BeTrue();
        item.TooltipText.Should().BeNull();
        item.ReadOnlyMessage.Should().BeEmpty();
    }

    [Test]
    public void ReadOnlyAttribute_ResourceProducesDimmedOpacityAndTooltip()
    {
        var writableState = WritableState.ReadOnlyAttribute;

        var item = new ResourcePickerItem(
            new ResourceKey("notes.txt"),
            Icon,
            writableState,
            readOnlyMessage: "File is read-only on disk.");

        item.IsReadOnly.Should().BeTrue();
        item.IsWritable.Should().BeFalse();
        item.TooltipText.Should().Be("File is read-only on disk.");
        item.ReadOnlyMessage.Should().Be("File is read-only on disk.");
    }

    [Test]
    public void ReadOnlyRoot_ResourceProducesDimmedOpacityAndTooltip()
    {
        var writableState = WritableState.ReadOnlyRoot;

        var item = new ResourcePickerItem(
            new ResourceKey("bundled:image.png"),
            Icon,
            writableState,
            readOnlyMessage: "Bundled package — read-only by design.");

        item.IsReadOnly.Should().BeTrue();
        item.IsWritable.Should().BeFalse();
        item.TooltipText.Should().Be("Bundled package — read-only by design.");
    }
}
