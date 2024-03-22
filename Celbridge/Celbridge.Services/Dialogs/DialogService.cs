using Celbridge.BaseLibrary.Dialogs;
using Celbridge.BaseLibrary.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Celbridge.Services.Dialogs;

public class DialogService : IDialogService
{
    public async Task<Result<string>> ShowFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        fileOpenPicker.FileTypeFilter.Add(".txt");

#if WINDOWS
        // For Uno.WinUI-based apps
        var navigationService = ServiceLocator.ServiceProvider.GetRequiredService<INavigationService>();
        var mainWindow = navigationService.MainWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(fileOpenPicker, hwnd);
#endif

        StorageFile file = await fileOpenPicker.PickSingleFileAsync();

        if (file == null)
        {
            return Result<string>.Fail("No file selected to open");
        }

        if (!File.Exists(file.Path))
        {
            return Result<string>.Fail("Selected file does not exist");
        }

        return Result<string>.Ok(file.Path);
    }
}
