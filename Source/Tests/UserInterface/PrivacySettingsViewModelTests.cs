using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.WebHost;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the Privacy section of the Settings page. The clear itself runs through the command
/// service, so the tests assert what the view model asks for rather than the state of any WebView.
/// </summary>
[TestFixture]
public class PrivacySettingsViewModelTests
{
    private ICommandService _commandService = null!;
    private IWebViewService _webViewService = null!;
    private IDialogService _dialogService = null!;
    private IStringLocalizer _stringLocalizer = null!;

    [SetUp]
    public void Setup()
    {
        _commandService = Substitute.For<ICommandService>();
        _webViewService = Substitute.For<IWebViewService>();
        _dialogService = Substitute.For<IDialogService>();

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _webViewService.CanClearBrowsingData.Returns(true);
        StubConfirmation(true);
        StubClearResult(Result.Ok());
    }

    [Test]
    public void PlatformCannotClear_DisablesTheActionAndSaysSo()
    {
        _webViewService.CanClearBrowsingData.Returns(false);

        var viewModel = CreateViewModel();

        viewModel.IsClearEnabled.Should().BeFalse();
        viewModel.IsStatusVisible.Should().BeTrue();
        viewModel.StatusMessage.Should().Be("Settings_Privacy_ClearUnavailable");
    }

    [Test]
    public async Task ConfirmedClear_RunsTheCommandAndReportsSuccess()
    {
        var viewModel = CreateViewModel();

        await viewModel.ConfirmClearBrowsingDataCommand.ExecuteAsync(null);

        await _commandService.Received(1).ExecuteAsync<IClearBrowsingDataCommand>(
            Arg.Any<Action<IClearBrowsingDataCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
        viewModel.StatusSeverity.Should().Be(StatusSeverity.Success);
        viewModel.StatusMessage.Should().Be("Settings_Privacy_Cleared");
        viewModel.IsClearEnabled.Should().BeTrue();
    }

    [Test]
    public async Task CancelledConfirmation_LeavesTheDataAlone()
    {
        StubConfirmation(false);

        var viewModel = CreateViewModel();

        await viewModel.ConfirmClearBrowsingDataCommand.ExecuteAsync(null);

        await _commandService.DidNotReceive().ExecuteAsync<IClearBrowsingDataCommand>(
            Arg.Any<Action<IClearBrowsingDataCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
        viewModel.IsStatusVisible.Should().BeFalse();
    }

    [Test]
    public async Task FailedClear_ReportsAnError()
    {
        StubClearResult(Result.Fail("Clear failed"));

        var viewModel = CreateViewModel();

        await viewModel.ConfirmClearBrowsingDataCommand.ExecuteAsync(null);

        viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        viewModel.StatusMessage.Should().Be("Settings_Privacy_ClearFailed");

        // A failed clear must not leave the button stuck disabled, so the user can retry.
        viewModel.IsClearEnabled.Should().BeTrue();
    }

    private PrivacySettingsViewModel CreateViewModel()
    {
        return new PrivacySettingsViewModel(
            new NullLogger<PrivacySettingsViewModel>(),
            _commandService,
            _webViewService,
            _dialogService,
            _stringLocalizer);
    }

    private void StubConfirmation(bool confirmed)
    {
        _dialogService.ShowConfirmationDialogAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(Task.FromResult(Result<bool>.Ok(confirmed)));
    }

    private void StubClearResult(Result result)
    {
        _commandService.ExecuteAsync<IClearBrowsingDataCommand>(
                Arg.Any<Action<IClearBrowsingDataCommand>?>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(Task.FromResult(result));
    }
}
