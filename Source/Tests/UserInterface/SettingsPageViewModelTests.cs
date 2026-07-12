using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels.Pages;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the Settings page view model. The theme selection round-trips through the real
/// SettingsService over an in-memory settings store fake, so the test asserts the stored value directly.
/// The theme apply is verified through a substitute IUserInterfaceService.
/// </summary>
[TestFixture]
public class SettingsPageViewModelTests
{
    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialStore _credentialStore = null!;
    private SettingsService _settingsService = null!;
    private IUserInterfaceService _userInterfaceService = null!;
    private SettingsPageViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();
        _credentialStore = new FakeCredentialStore();

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        _settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            _settingsStore,
            _credentialStore,
            workspaceWrapper);

        _userInterfaceService = Substitute.For<IUserInterfaceService>();

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new SettingsPageViewModel(
            _settingsService,
            stringLocalizer,
            _userInterfaceService);
    }

    [Test]
    public void ThemeOptions_CoverEveryThemeInOrder()
    {
        var themes = _viewModel.ThemeOptions.Select(themeOption => themeOption.Theme);

        themes.Should().Equal(
            ApplicationColorTheme.System,
            ApplicationColorTheme.Light,
            ApplicationColorTheme.Dark);
    }

    [Test]
    public void SelectedTheme_InitialisesToStoredTheme()
    {
        // The stored theme defaults to System when nothing has been set.
        _viewModel.SelectedTheme.Should().NotBeNull();
        _viewModel.SelectedTheme!.Theme.Should().Be(ApplicationColorTheme.System);
    }

    [Test]
    public void SelectingTheme_PersistsAndAppliesIt()
    {
        var darkOption = _viewModel.ThemeOptions.First(themeOption => themeOption.Theme == ApplicationColorTheme.Dark);

        _viewModel.SelectedTheme = darkOption;

        _settingsService.Get(SettingCatalog.Application.Theme).Should().Be(ApplicationColorTheme.Dark);
        _userInterfaceService.Received(1).ApplyCurrentTheme();
    }

    [Test]
    public void InitialisingViewModel_DoesNotPersistOrApplyATheme()
    {
        // Reflecting the stored theme in the combo box must not run the changed handler; construction
        // happened in Setup, so no apply should have occurred.
        _userInterfaceService.DidNotReceive().ApplyCurrentTheme();
    }
}
