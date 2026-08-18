using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Messaging;
using Celbridge.Reports;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostDialog RPC methods for contribution document views.
/// Provides image picking, file picking, alert dialogs, and workspace notifications.
/// </summary>
internal sealed class CustomDialogHandler : IHostDialog
{
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IMessengerService _messengerService;
    private readonly CustomDocumentViewModel _viewModel;

    public CustomDialogHandler(
        IDialogService dialogService,
        IStringLocalizer stringLocalizer,
        IMessengerService messengerService,
        CustomDocumentViewModel viewModel)
    {
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
        _messengerService = messengerService;
        _viewModel = viewModel;
    }

    public async Task<PickImageResult> PickImageAsync(IReadOnlyList<string>? extensions = null)
    {
        var extensionsArray = extensions?.ToArray();
        if (extensionsArray is null || extensionsArray.Length == 0)
        {
            extensionsArray =
            [
                ".png",
                ".jpg",
                ".jpeg",
                ".gif",
                ".webp",
                ".svg",
                ".bmp"
            ];
        }

        var title = _stringLocalizer.GetString("Extension_SelectImage_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title, showPreview: true);

        if (result.IsSuccess)
        {
            // GetRelativePathFromResourceKey treats its input as the bare path
            // portion of a project-rooted resource key, so we pass .Path to skip
            // the canonical "project:" prefix that ToString() now emits.
            var resourcePath = result.Value.Path;
            var relativePath = _viewModel.GetRelativePathFromResourceKey(resourcePath);
            return new PickImageResult(relativePath);
        }

        return new PickImageResult(null);
    }

    public async Task<PickFileResult> PickFileAsync(IReadOnlyList<string>? extensions = null)
    {
        var title = _stringLocalizer.GetString("Extension_SelectFile_Title");
        var extensionsArray = extensions?.ToArray() ?? [];
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title);

        if (result.IsSuccess)
        {
            var resourcePath = result.Value.Path;
            var relativePath = _viewModel.GetRelativePathFromResourceKey(resourcePath);
            return new PickFileResult(relativePath);
        }

        return new PickFileResult(null);
    }

    public async Task<AlertResult> AlertAsync(string title, string message)
    {
        await _dialogService.ShowAlertDialogAsync(title, message);
        return new AlertResult();
    }

    public async Task<NotifyResult> NotifyAsync(string severity, string message)
    {
        await Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Notification message is empty.", nameof(message));
        }

        // A value this build does not know is a mistake in the editor rather than a newer severity to
        // tolerate, and quietly showing an intended error as information is the worse failure.
        if (!TryParseSeverity(severity, out var parsedSeverity))
        {
            throw new ArgumentException(
                $"Unknown notification severity: '{severity}'. Expected 'info', 'warning' or 'error'.",
                nameof(severity));
        }

        var notification = new EditorNotificationMessage(parsedSeverity, message);
        _messengerService.Send(notification);

        return new NotifyResult();
    }

    private static bool TryParseSeverity(string severity, out ReportSeverity parsedSeverity)
    {
        switch (severity?.ToLowerInvariant())
        {
            case "info":
                parsedSeverity = ReportSeverity.Info;
                return true;

            case "warning":
                parsedSeverity = ReportSeverity.Warning;
                return true;

            case "error":
                parsedSeverity = ReportSeverity.Error;
                return true;

            default:
                parsedSeverity = ReportSeverity.Info;
                return false;
        }
    }
}
