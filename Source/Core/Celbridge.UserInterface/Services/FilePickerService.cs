using Celbridge.Dialog;
using Celbridge.Platform;
using Windows.Storage.Pickers;

namespace Celbridge.UserInterface.Services;

public class FilePickerService : IFilePickerService
{
    private readonly ILocalFileSystem _fileSystem;
    private readonly IPlatformInfo _platformInfo;

    public FilePickerService(ILocalFileSystem fileSystem, IPlatformInfo platformInfo)
    {
        _fileSystem = fileSystem;
        _platformInfo = platformInfo;
    }

    public async Task<Result<string>> PickSingleFileAsync(
        IEnumerable<string> extensions,
        PickerStartFolder startFolder = PickerStartFolder.Documents,
        string settingsIdentifier = "")
    {
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = GetPickerLocationId(startFolder),
            SettingsIdentifier = settingsIdentifier
        };

        foreach (var extension in extensions)
        {
            fileOpenPicker.FileTypeFilter.Add(extension);
        }

        InitializeWithMainWindow(fileOpenPicker);

        StorageFile file = await fileOpenPicker.PickSingleFileAsync();

        if (file == null)
        {
            return Result<string>.Fail("No file selected to open");
        }

        var infoResult = await _fileSystem.GetInfoAsync(file.Path);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result<string>.Fail("Selected file does not exist");
        }

        return Result<string>.Ok(file.Path);
    }

    public async Task<Result<string>> PickSingleFolderAsync(
        PickerStartFolder startFolder = PickerStartFolder.Documents,
        string settingsIdentifier = "")
    {
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = GetPickerLocationId(startFolder),
            SettingsIdentifier = settingsIdentifier
        };

        folderPicker.FileTypeFilter.Add("*");

        InitializeWithMainWindow(folderPicker);

        StorageFolder folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null)
        {
            return Result<string>.Fail("No folder selected");
        }

        var folderPath = folder.Path;

        return Result<string>.Ok(folderPath);
    }

    public async Task<Result<string>> PickSaveFileAsync(
        string suggestedFileName,
        string fileTypeDescription,
        IEnumerable<string> fileExtensions,
        PickerStartFolder startFolder = PickerStartFolder.Documents,
        string settingsIdentifier = "")
    {
        var fileSavePicker = new FileSavePicker
        {
            SuggestedStartLocation = GetPickerLocationId(startFolder),
            SuggestedFileName = suggestedFileName,
            SettingsIdentifier = settingsIdentifier
        };

        var fileTypes = GetFileTypes(fileExtensions);
        if (fileTypes.Count > 0)
        {
            fileSavePicker.FileTypeChoices.Add(fileTypeDescription, fileTypes);
        }

        InitializeWithMainWindow(fileSavePicker);

        StorageFile file = await fileSavePicker.PickSaveFileAsync();
        if (file == null)
        {
            return Result<string>.Fail("No file selected to save");
        }

        return Result<string>.Ok(file.Path);
    }

    // No extensions means any name, which takes the platform's own file type.
    internal List<string> GetFileTypes(IEnumerable<string> fileExtensions)
    {
        var extensions = fileExtensions.ToList();
        if (extensions.Count > 0)
        {
            return extensions;
        }

        return _platformInfo.SaveDialogAnyNameFileType is string anyNameFileType
            ? [anyNameFileType]
            : [];
    }

    // An unspecified location leaves the folder to the platform, which opens the folder last used under the
    // settings identifier. A save dialog that requires a start location gets Downloads. That dialog treats the
    // location as a suggestion, and opens the folder the user last navigated to under the settings identifier.
    internal PickerLocationId GetPickerLocationId(PickerStartFolder startFolder)
    {
        if (startFolder == PickerStartFolder.LastUsed)
        {
            return _platformInfo.SaveDialogRequiresStartLocation
                ? PickerLocationId.Downloads
                : PickerLocationId.Unspecified;
        }

        return startFolder == PickerStartFolder.Downloads
            ? PickerLocationId.Downloads
            : PickerLocationId.DocumentsLibrary;
    }

    // The packaged WinUI head requires a picker to be associated with the owning window handle.
    private void InitializeWithMainWindow(object picker)
    {
        if (!_platformInfo.PickersRequireWindowHandle)
        {
            return;
        }

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        var mainWindow = userInterfaceService.MainWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}
