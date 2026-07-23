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
    public void Writable_ResourceProducesFullOpacityNoTooltipNoHelpText()
    {
        var resource = Substitute.For<IResource>();
        resource.WritableState.Returns(WritableState.Writable);

        var item = new ResourcePickerItem(resource, new ResourceKey("docs/photo.png"), Icon);

        item.IsReadOnly.Should().BeFalse();
        item.NameOpacity.Should().Be(1.0);
        item.TooltipText.Should().BeNull();
        item.ReadOnlyMessage.Should().BeEmpty();
    }

    [Test]
    public void Locked_ResourceProducesDimmedOpacityAndTooltip()
    {
        var resource = Substitute.For<IResource>();
        resource.WritableState.Returns(WritableState.Locked);

        var item = new ResourcePickerItem(
            resource,
            new ResourceKey("Data/frozen.bin"),
            Icon,
            readOnlyMessage: "Locked by project configuration.");

        item.IsReadOnly.Should().BeTrue();
        item.NameOpacity.Should().Be(0.5);
        item.TooltipText.Should().Be("Locked by project configuration.");
        item.ReadOnlyMessage.Should().Be("Locked by project configuration.");
    }

    [Test]
    public void ReadOnlyAttribute_ResourceProducesDimmedOpacityAndTooltip()
    {
        var resource = Substitute.For<IResource>();
        resource.WritableState.Returns(WritableState.ReadOnlyAttribute);

        var item = new ResourcePickerItem(
            resource,
            new ResourceKey("notes.txt"),
            Icon,
            readOnlyMessage: "File is read-only on disk.");

        item.IsReadOnly.Should().BeTrue();
        item.NameOpacity.Should().Be(0.5);
        item.TooltipText.Should().Be("File is read-only on disk.");
    }

    [Test]
    public void ReadOnlyRoot_ResourceProducesDimmedOpacityAndTooltip()
    {
        var resource = Substitute.For<IResource>();
        resource.WritableState.Returns(WritableState.ReadOnlyRoot);

        var item = new ResourcePickerItem(
            resource,
            new ResourceKey("bundled:image.png"),
            Icon,
            readOnlyMessage: "Bundled package — read-only by design.");

        item.IsReadOnly.Should().BeTrue();
        item.NameOpacity.Should().Be(0.5);
        item.TooltipText.Should().Be("Bundled package — read-only by design.");
    }
}
