using Celbridge.FilePicker;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Celbridge.UserInterface.Services;

public class FilePickerService : IFilePickerService
{
    public async Task<Result<string>> PickSingleFileAsync(IEnumerable<string> extensions)
    {
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        foreach (var extension in extensions)
        {
            fileOpenPicker.FileTypeFilter.Add(extension);
        }

#if WINDOWS
        // For Uno.WinUI-based apps
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        var mainWindow = userInterfaceService.MainWindow;
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

    public async Task<Result<string>> PickSingleFolderAsync()
    {
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        folderPicker.FileTypeFilter.Add("*");

#if WINDOWS
        // For Uno.WinUI-based apps
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        var mainWindow = userInterfaceService.MainWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
#endif

        StorageFolder folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null)
        {
            return Result<string>.Fail("No folder selected");
        }

        var folderPath = folder.Path;

        return Result<string>.Ok(folderPath);
    }

}
