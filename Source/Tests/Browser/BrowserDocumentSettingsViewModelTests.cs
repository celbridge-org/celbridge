using Celbridge.UserInterface.Views.Controls;
using Celbridge.Browser.ViewModels;

namespace Celbridge.Tests.Browser;

/// <summary>
/// Unit tests for the browser settings surface's section rail. The sections carry stand-in content, because the surface
/// chooses each section's control.
/// </summary>
[TestFixture]
public class BrowserDocumentSettingsViewModelTests
{
    private static readonly SettingsSection Bookmarks =
        new("Bookmarks", "bs-star", "Bookmarks", "Pages this document offers.", "bookmarks-content");

    private static readonly SettingsSection BrowsingData =
        new("BrowsingData", "bs-clock-history", "Browsing Data", "What the web profile keeps.", "browsing-data-content");

    private static readonly SettingsSection Other =
        new("Other", "bs-palette", "Other", "Another section.", "other-content");

    private static BrowserDocumentSettingsViewModel CreateViewModel(string selectedSectionKey = "")
    {
        var viewModel = new BrowserDocumentSettingsViewModel();
        viewModel.InitializeSections([Bookmarks, BrowsingData, Other], selectedSectionKey);

        return viewModel;
    }

    [Test]
    public void WithNoStoredKey_TheFirstSectionIsSelected()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSection.Should().Be(Bookmarks);
        viewModel.SelectedSectionKey.Should().Be("Bookmarks");
    }

    [Test]
    public void AStoredKey_SelectsThatSection()
    {
        var viewModel = CreateViewModel("Other");

        viewModel.SelectedSection.Should().Be(Other);
    }

    [Test]
    public void AnUnknownStoredKey_FallsBackToTheFirstSection()
    {
        // A renamed or removed section drops a returning user on the first section.
        var viewModel = CreateViewModel("Home");

        viewModel.SelectedSection.Should().Be(Bookmarks);
    }

    [Test]
    public void SelectingASection_MarksOnlyThatOne()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSection = BrowsingData;

        Bookmarks.IsSelected.Should().BeFalse();
        BrowsingData.IsSelected.Should().BeTrue();
        Other.IsSelected.Should().BeFalse();
    }

    [Test]
    public void ClearingTheSelection_PutsTheLastSectionBack()
    {
        // The property is public, and a section is always showing.
        var viewModel = CreateViewModel();
        viewModel.SelectedSection = BrowsingData;

        viewModel.SelectedSection = null;

        viewModel.SelectedSection.Should().Be(BrowsingData);
    }

    [Test]
    public void SelectSection_ByKey_ShowsThatSection()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectSection("BrowsingData");

        viewModel.SelectedSection.Should().Be(BrowsingData);
    }

    [Test]
    public void SelectSection_WithAnUnknownKey_LeavesTheSelectionAlone()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectSection("Retired");

        viewModel.SelectedSection.Should().Be(Bookmarks);
    }
}
