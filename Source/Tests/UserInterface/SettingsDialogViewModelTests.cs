using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Settings;
using Celbridge.UserInterface.ViewModels.Dialogs;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the settings dialog's category rail. The selected category is persisted by key through
/// the real SettingsService over an in-memory settings store fake, so a reordered rail can be exercised
/// against a key stored by an earlier run. The categories carry stand-in content, since which control a
/// category shows is the dialog's business rather than the view model's.
/// </summary>
[TestFixture]
public class SettingsDialogViewModelTests
{
    private SettingsService _settingsService = null!;

    private static readonly SettingsSection Appearance =
        new("Appearance", "bs-palette", "Appearance", "How Celbridge looks.", "appearance-content");

    private static readonly SettingsSection Workshop =
        new("Workshop", "bs-shop", "Workshop", "Connect to a Workshop.", "workshop-content");

    private static readonly SettingsSection WebView =
        new("WebView", "bs-globe", "Web View", "How web content behaves.", "webview-content");

    [SetUp]
    public void Setup()
    {
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        _settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            new FakeSettingsStore(),
            new FakeCredentialStore(),
            workspaceWrapper);
    }

    [Test]
    public void WithNothingStored_TheFirstCategoryIsSelected()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSection.Should().Be(Appearance);
    }

    [Test]
    public void SelectingACategory_PersistsItsKey()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSection = WebView;

        var storedKey = _settingsService.Get(SettingCatalog.Application.SettingsDialogSelectedSection);
        storedKey.Should().Be("WebView");
    }

    [Test]
    public void ReopeningTheDialog_RestoresTheStoredCategory()
    {
        _settingsService.Set(SettingCatalog.Application.SettingsDialogSelectedSection, "Workshop");

        var viewModel = CreateViewModel();

        viewModel.SelectedSection.Should().Be(Workshop);
    }

    [Test]
    public void AfterTheRailIsReordered_TheStoredKeyStillFindsItsCategory()
    {
        // The key rather than the position is what is persisted, so a category that has moved is still
        // the one the returning user lands on.
        _settingsService.Set(SettingCatalog.Application.SettingsDialogSelectedSection, "WebView");

        var reorderedSections = new List<SettingsSection>
        {
            WebView,
            Appearance,
            Workshop,
        };

        var viewModel = new SettingsDialogViewModel(_settingsService);
        viewModel.InitializeSections(reorderedSections);

        viewModel.SelectedSection.Should().Be(WebView);
    }

    [Test]
    public void AnUnknownStoredKey_FallsBackWithoutOverwritingTheKey()
    {
        // Falling back is not the user choosing a category. Writing here would discard a key that may
        // name a category this build simply does not have.
        _settingsService.Set(SettingCatalog.Application.SettingsDialogSelectedSection, "Retired");

        var viewModel = CreateViewModel();

        viewModel.SelectedSection.Should().Be(Appearance);

        var storedKey = _settingsService.Get(SettingCatalog.Application.SettingsDialogSelectedSection);
        storedKey.Should().Be("Retired");
    }

    [Test]
    public void ClearingTheSelection_PutsTheLastCategoryBack()
    {
        // A single-selection list still lets the user deselect, which would leave every binding in the
        // content pane resolving to null and the dialog showing nothing.
        var viewModel = CreateViewModel();
        viewModel.SelectedSection = Workshop;

        viewModel.SelectedSection = null;

        viewModel.SelectedSection.Should().Be(Workshop);
    }

    [Test]
    public void SelectingACategory_MarksOnlyThatOneSelected()
    {
        // The rail rows take their checked state from this flag, so exactly one may carry it.
        var viewModel = CreateViewModel();

        viewModel.SelectedSection = WebView;

        viewModel.Sections.Where(section => section.IsSelected).Should().Equal(WebView);
    }

    [Test]
    public void InitializeSections_RejectsAnEmptyRail()
    {
        var viewModel = new SettingsDialogViewModel(_settingsService);

        var initialize = () => viewModel.InitializeSections(Array.Empty<SettingsSection>());

        initialize.Should().Throw<InvalidOperationException>();
    }

    private SettingsDialogViewModel CreateViewModel()
    {
        var sections = new List<SettingsSection>
        {
            Appearance,
            Workshop,
            WebView,
        };

        var viewModel = new SettingsDialogViewModel(_settingsService);
        viewModel.InitializeSections(sections);

        return viewModel;
    }
}
