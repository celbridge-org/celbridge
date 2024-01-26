using CommunityToolkit.Mvvm.Messaging;

namespace CelLegacy.Services;

public interface IDialogService
{
    XamlRoot? XamlRoot { get; set; }

    Task ShowNewProjectDialogAsync();
    Task ShowSettingsDialogAsync();
    Task ShowAddResourceDialogAsync();
    bool IsProgressDialogActive { get; }
    Result ShowProgressDialog(string title, Action? onCancel);
    Result HideProgressDialog();
    void OpenFileExplorer(string folder);
}

public record HideProgressDialogMessage;

public class DialogService : IDialogService
{
    private IMessenger _messengerService;

    public XamlRoot? XamlRoot { get; set; }

    public DialogService(IMessenger messengerService)
    {
        _messengerService = messengerService;
    }

    public async Task ShowNewProjectDialogAsync()
    {
        var dialog = new NewProjectDialog();
        dialog.XamlRoot = XamlRoot;

        await dialog.ShowAsync();
    }

    public async Task ShowSettingsDialogAsync()
    {
        var dialog = new SettingsDialog();
        dialog.XamlRoot = XamlRoot;

        await dialog.ShowAsync();
    }

    public async Task ShowAddResourceDialogAsync()
    {
        var dialog = new AddResourceDialog();
        dialog.XamlRoot = XamlRoot;

        await dialog.ShowAsync();
    }
    public bool IsProgressDialogActive { get; private set; }

    public Result ShowProgressDialog(string title, Action? onCancel)
    {
        if (IsProgressDialogActive)
        {
            return new ErrorResult($"Failed to show Progress Dialog '{title}'. A Progress Dialog is already active.");
        }

        async Task ShowDialog()
        {
            IsProgressDialogActive = true;

            var dialog = new ProgressDialog(onCancel);
            dialog.XamlRoot = XamlRoot;
            dialog.ViewModel.Title = title;

            await dialog.ShowAsync();

            IsProgressDialogActive = false;
        }
        _ = ShowDialog();

        return new SuccessResult();
    }

    public Result HideProgressDialog()
    {
        if (!IsProgressDialogActive)
        {
            return new ErrorResult($"Failed to hide Progress Dialog. No Progress Dialog is active.");
        }

        var message = new HideProgressDialogMessage();
        _messengerService.Send(message);

        return new SuccessResult();
    }


    public void OpenFileExplorer(string folder)
    {
        var startInfo = new ProcessStartInfo();

        // Todo: Use "open" on MacOS and "xdg-open" on Linux
        startInfo.FileName = "explorer.exe";
        startInfo.Arguments = folder;
        Process.Start(startInfo);
    }
}
