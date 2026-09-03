using Celbridge.UserInterface.Platform;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the tag the native Edit menu gives each generated verb item. Resolution is a pure mapping
/// from a tag to the shortcut table, so these run on every platform.
/// </summary>
[TestFixture]
public class MacOSMainMenuTests
{
    [Test]
    public void ResolveEditShortcut_ForAGeneratedTag_ReturnsTheShortcutItStandsFor()
    {
        for (var index = 0; index < MacOSEditShortcuts.All.Count; index++)
        {
            var tag = MacOSMainMenu.TagEditVerbBase + index;

            MacOSMainMenu.ResolveEditShortcut(tag).Should().BeSameAs(MacOSEditShortcuts.All[index]);
        }
    }

    [Test]
    public void ResolveEditShortcut_ForATagBelowTheGeneratedRange_ReturnsNull()
    {
        // Fixed command tags sit below the base.
        MacOSMainMenu.ResolveEditShortcut(MacOSMainMenu.TagEditVerbBase - 1).Should().BeNull();
        MacOSMainMenu.ResolveEditShortcut(0).Should().BeNull();
    }

    [Test]
    public void ResolveEditShortcut_ForATagAboveTheGeneratedRange_ReturnsNull()
    {
        // Recent project tags sit above the range.
        var firstTagPastTheRange = MacOSMainMenu.TagEditVerbBase + MacOSEditShortcuts.All.Count;

        MacOSMainMenu.ResolveEditShortcut(firstTagPastTheRange).Should().BeNull();
        MacOSMainMenu.ResolveEditShortcut(1000).Should().BeNull();
    }
}
