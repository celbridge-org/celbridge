using Celbridge.UserInterface.Views.Controls;
using Celbridge.WebView.ViewModels;

namespace Celbridge.Tests.WebView;

/// <summary>
/// Unit tests for the Web View settings surface's section rail. The sections carry stand-in content, since
/// which control a section shows is the surface's business rather than the view model's.
/// </summary>
[TestFixture]
public class WebViewDocumentSettingsViewModelTests
{
    private static readonly SettingsSection Home =
        new("Home", "bs-house", "Home", "The page this document opens on.", "home-content");

    private static readonly SettingsSection Bookmarks =
        new("Bookmarks", "bs-bookmark", "Bookmarks", "Pages this document offers.", "bookmarks-content");

    private static readonly SettingsSection Appearance =
        new("Appearance", "bs-palette", "Appearance", "The chrome around the page.", "appearance-content");

    private static WebViewDocumentSettingsViewModel CreateViewModel(string selectedSectionKey = "")
    {
        var viewModel = new WebViewDocumentSettingsViewModel();
        viewModel.InitializeSections([Home, Bookmarks, Appearance], selectedSectionKey);

        return viewModel;
    }

    [Test]
    public void WithNoStoredKey_TheFirstSectionIsSelected()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSection.Should().Be(Home);
        viewModel.SelectedSectionKey.Should().Be("Home");
    }

    [Test]
    public void AStoredKey_SelectsThatSection()
    {
        var viewModel = CreateViewModel("Appearance");

        viewModel.SelectedSection.Should().Be(Appearance);
    }

    [Test]
    public void AnUnknownStoredKey_FallsBackToTheFirstSection()
    {
        // A renamed or removed section drops a returning user on the first one rather than on nothing.
        var viewModel = CreateViewModel("Retired");

        viewModel.SelectedSection.Should().Be(Home);
    }

    [Test]
    public void SelectingASection_MarksOnlyThatOne()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSection = Bookmarks;

        Home.IsSelected.Should().BeFalse();
        Bookmarks.IsSelected.Should().BeTrue();
        Appearance.IsSelected.Should().BeFalse();
    }

    [Test]
    public void ClearingTheSelection_PutsTheLastSectionBack()
    {
        // Nothing in the rail clears the selection, but the property is public: a section is always showing.
        var viewModel = CreateViewModel();
        viewModel.SelectedSection = Bookmarks;

        viewModel.SelectedSection = null;

        viewModel.SelectedSection.Should().Be(Bookmarks);
    }

    [Test]
    public void SelectSection_ByKey_ShowsThatSection()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectSection("Bookmarks");

        viewModel.SelectedSection.Should().Be(Bookmarks);
    }

    [Test]
    public void SelectSection_WithAnUnknownKey_LeavesTheSelectionAlone()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectSection("Retired");

        viewModel.SelectedSection.Should().Be(Home);
    }
}
