using Celbridge.Commands;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.WebHost;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the Privacy section of the Settings dialog. The clear is confirmed in place rather than
/// through a dialog, and runs through the command service, so the tests assert what the view model asks for
/// rather than the state of any WebView.
/// </summary>
[TestFixture]
public class WebViewSettingsViewModelTests
{
    private ICommandService _commandService = null!;
    private IWebViewService _webViewService = null!;
    private IStringLocalizer _stringLocalizer = null!;

    [SetUp]
    public void Setup()
    {
        _commandService = Substitute.For<ICommandService>();
        _webViewService = Substitute.For<IWebViewService>();

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _webViewService.CanClearBrowsingData.Returns(true);
        StubClearResult(Result.Ok());
    }

    [Test]
    public void PlatformCannotClear_DisablesTheActionAndSaysSo()
    {
        _webViewService.CanClearBrowsingData.Returns(false);

        var viewModel = CreateViewModel();

        viewModel.IsClearEnabled.Should().BeFalse();
        viewModel.IsStatusVisible.Should().BeTrue();
        viewModel.StatusMessage.Should().Be("Settings_WebView_ClearUnavailable");
    }

    [Test]
    public void ClearButton_ShowsTheConfirmationInsteadOfClearing()
    {
        var viewModel = CreateViewModel();

        viewModel.BeginClearBrowsingDataCommand.Execute(null);

        viewModel.IsConfirmingClear.Should().BeTrue();
        viewModel.IsClearButtonVisible.Should().BeFalse();
        _commandService.DidNotReceive().ExecuteImmediate<IClearBrowsingDataCommand>(
            Arg.Any<Action<IClearBrowsingDataCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public async Task ConfirmedClear_RunsTheCommandAndReportsSuccess()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginClearBrowsingDataCommand.Execute(null);

        await viewModel.ConfirmClearBrowsingDataCommand.ExecuteAsync(null);

        viewModel.IsConfirmingClear.Should().BeFalse();

        await _commandService.Received(1).ExecuteImmediate<IClearBrowsingDataCommand>(
            Arg.Any<Action<IClearBrowsingDataCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
        viewModel.StatusSeverity.Should().Be(StatusSeverity.Success);
        viewModel.StatusMessage.Should().Be("Settings_WebView_Cleared");
        viewModel.IsClearEnabled.Should().BeTrue();
    }

    [Test]
    public void CancelledConfirmation_LeavesTheDataAlone()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginClearBrowsingDataCommand.Execute(null);

        viewModel.CancelClearBrowsingDataCommand.Execute(null);

        viewModel.IsConfirmingClear.Should().BeFalse();
        viewModel.IsClearButtonVisible.Should().BeTrue();
        _commandService.DidNotReceive().ExecuteImmediate<IClearBrowsingDataCommand>(
            Arg.Any<Action<IClearBrowsingDataCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
        viewModel.IsStatusVisible.Should().BeFalse();
    }

    [Test]
    public async Task FailedClear_ReportsAnError()
    {
        StubClearResult(Result.Fail("Clear failed"));

        var viewModel = CreateViewModel();
        viewModel.BeginClearBrowsingDataCommand.Execute(null);

        await viewModel.ConfirmClearBrowsingDataCommand.ExecuteAsync(null);

        viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        viewModel.StatusMessage.Should().Be("Settings_WebView_ClearFailed");

        // A failed clear must not leave the button stuck disabled, so the user can retry.
        viewModel.IsClearEnabled.Should().BeTrue();
    }

    private WebViewSettingsViewModel CreateViewModel()
    {
        return new WebViewSettingsViewModel(
            new NullLogger<WebViewSettingsViewModel>(),
            _commandService,
            _webViewService,
            _stringLocalizer);
    }

    private void StubClearResult(Result result)
    {
        _commandService.ExecuteImmediate<IClearBrowsingDataCommand>(
                Arg.Any<Action<IClearBrowsingDataCommand>?>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(Task.FromResult(result));
    }
}
