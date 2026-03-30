using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostDialog RPC methods for contribution document views.
/// Provides image picking, file picking, and alert dialogs.
/// </summary>
internal sealed class ContributionDialogHandler : IHostDialog
{
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ContributionDocumentViewModel _viewModel;

    public ContributionDialogHandler(
        IDialogService dialogService,
        IStringLocalizer stringLocalizer,
        ContributionDocumentViewModel viewModel)
    {
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
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
            var resourceKey = result.Value.ToString();
            var relativePath = _viewModel.GetRelativePathFromResourceKey(resourceKey);
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
            var resourceKey = result.Value.ToString();
            var relativePath = _viewModel.GetRelativePathFromResourceKey(resourceKey);
            return new PickFileResult(relativePath);
        }

        return new PickFileResult(null);
    }

    public async Task<AlertResult> AlertAsync(string title, string message)
    {
        await _dialogService.ShowAlertDialogAsync(title, message);
        return new AlertResult();
    }
}
