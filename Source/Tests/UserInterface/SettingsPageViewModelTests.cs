using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
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
/// Unit tests for the Settings page view model. The stored theme is read through the real SettingsService
/// over an in-memory settings store fake. Selecting a theme dispatches ISetThemeCommand rather than writing
/// the setting here, so that is asserted against a substitute command service. A real MessengerService
/// carries the theme-changed broadcast the page follows.
/// </summary>
[TestFixture]
public class SettingsPageViewModelTests
{
    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialStore _credentialStore = null!;
    private SettingsService _settingsService = null!;
    private ICommandService _commandService = null!;
    private IMessengerService _messengerService = null!;
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

        _commandService = Substitute.For<ICommandService>();
        _messengerService = new MessengerService();

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new SettingsPageViewModel(
            _settingsService,
            stringLocalizer,
            _commandService,
            _messengerService);
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
    public void SelectingTheme_DispatchesSetThemeCommandWithThatTheme()
    {
        var darkOption = _viewModel.ThemeOptions.First(themeOption => themeOption.Theme == ApplicationColorTheme.Dark);

        _viewModel.SelectedTheme = darkOption;

        _commandService.Received(1).Execute<ISetThemeCommand>(
            Arg.Is<Action<ISetThemeCommand>?>(configure => ConfiguresTheme(configure, ApplicationColorTheme.Dark)),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public void InitialisingViewModel_DoesNotSetATheme()
    {
        // Reflecting the stored theme in the combo box must not run the changed handler; construction
        // happened in Setup, so no command should have been dispatched.
        _commandService.DidNotReceive().Execute<ISetThemeCommand>(
            Arg.Any<Action<ISetThemeCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public void ThemeChangedElsewhere_UpdatesTheSelectionWithoutDispatching()
    {
        // The View menu offers the same themes, so a change made there while this page is open has to be
        // reflected here rather than leaving a stale selection.
        _viewModel.OnLoaded();

        _settingsService.Set(SettingCatalog.Application.Theme, ApplicationColorTheme.Light);
        _messengerService.Send(new ThemeChangedMessage(UserInterfaceTheme.Light));

        _viewModel.SelectedTheme!.Theme.Should().Be(ApplicationColorTheme.Light);

        // Following the change must not dispatch a command of its own, which would loop back through the
        // theme service.
        _commandService.DidNotReceive().Execute<ISetThemeCommand>(
            Arg.Any<Action<ISetThemeCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public void AfterUnloading_ThemeChangesAreNoLongerFollowed()
    {
        _viewModel.OnLoaded();
        _viewModel.OnUnloaded();

        _settingsService.Set(SettingCatalog.Application.Theme, ApplicationColorTheme.Light);
        _messengerService.Send(new ThemeChangedMessage(UserInterfaceTheme.Light));

        _viewModel.SelectedTheme!.Theme.Should().Be(ApplicationColorTheme.System);
    }

    /// <summary>
    /// Runs a captured configure action against a stand-in command to see which theme it would set.
    /// </summary>
    private static bool ConfiguresTheme(Action<ISetThemeCommand>? configure, ApplicationColorTheme expectedTheme)
    {
        if (configure is null)
        {
            return false;
        }

        var command = Substitute.For<ISetThemeCommand>();
        configure(command);

        return command.Theme == expectedTheme;
    }
}
