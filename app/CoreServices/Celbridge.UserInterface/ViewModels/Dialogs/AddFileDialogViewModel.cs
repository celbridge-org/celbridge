using Celbridge.Explorer;
using Celbridge.Settings;
using Celbridge.Validators;
using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public record FileTypeItem(string DisplayName, ResourceFormat Format, string Extension);

public partial class AddFileDialogViewModel : ObservableObject
{
    private readonly IEditorSettings _editorSettings;
    private readonly IStringLocalizer _stringLocalizer;
    private bool _isUpdatingFromCode;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isFileNameValid = false;

    [ObservableProperty]
    private bool _isSubmitEnabled = false;

    [ObservableProperty]
    private int _selectedFileTypeIndex;

    public IValidator? Validator { get; set; }

    public List<FileTypeItem> FileTypes { get; }

    /// <summary>
    /// Index of the "Other" option in the file types list.
    /// </summary>
    public int OtherFileTypeIndex => FileTypes.Count - 1;

    public ResourceFormat SelectedFileType => FileTypes[SelectedFileTypeIndex].Format;

    /// <summary>
    /// Gets the previously saved file extension from settings.
    /// </summary>
    public string PreviousFileExtension => _editorSettings.PreviousNewFileExtension;

    public AddFileDialogViewModel(
        IEditorSettings editorSettings,
        IStringLocalizer stringLocalizer)
    {
        _editorSettings = editorSettings;
        _stringLocalizer = stringLocalizer;

        FileTypes =
        [
            new FileTypeItem(_stringLocalizer.GetString("AddFileDialog_FileType_Python"), ResourceFormat.Python, ExplorerConstants.PythonExtension),
            new FileTypeItem(_stringLocalizer.GetString("AddFileDialog_FileType_Excel"), ResourceFormat.Excel, ExplorerConstants.ExcelExtension),
            new FileTypeItem(_stringLocalizer.GetString("AddFileDialog_FileType_Markdown"), ResourceFormat.Markdown, ExplorerConstants.MarkdownExtension),
            new FileTypeItem(_stringLocalizer.GetString("AddFileDialog_FileType_WebApp"), ResourceFormat.WebApp, ExplorerConstants.WebAppExtension),
            new FileTypeItem(_stringLocalizer.GetString("AddFileDialog_FileType_Text"), ResourceFormat.Text, ExplorerConstants.TextExtension),
            new FileTypeItem(_stringLocalizer.GetString("AddFileDialog_FileType_Other"), ResourceFormat.Text, string.Empty),
        ];

        // Select the dropdown based on the previously saved extension
        var previousExtension = _editorSettings.PreviousNewFileExtension;
        var index = FileTypes.FindIndex(ft =>
            !string.IsNullOrEmpty(ft.Extension) &&
            ft.Extension.Equals(previousExtension, StringComparison.OrdinalIgnoreCase));
        
        // If extension matches a known type, select it; otherwise select "Other"
        _selectedFileTypeIndex = index >= 0 ? index : OtherFileTypeIndex;

        PropertyChanged += AddFileDialogViewModel_PropertyChanged;
    }

    private void AddFileDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingFromCode)
        {
            return;
        }

        if (e.PropertyName == nameof(SelectedFileTypeIndex))
        {
            // Update the file extension when the file type changes (but not for "Other")
            UpdateFileExtension();
            UpdateValidationState();
        }
        else if (e.PropertyName == nameof(FileName))
        {
            // Check if the user typed an extension that doesn't match the selected type
            SyncDropdownToExtension();
            UpdateValidationState();
        }
    }

    private void UpdateFileExtension()
    {
        if (string.IsNullOrEmpty(FileName))
        {
            return;
        }

        // Don't modify the extension if "Other" is selected
        if (SelectedFileTypeIndex == OtherFileTypeIndex)
        {
            return;
        }

        var selectedFileType = FileTypes[SelectedFileTypeIndex];
        var currentExtension = Path.GetExtension(FileName);

        if (!string.IsNullOrEmpty(currentExtension))
        {
            // Replace the existing extension with the new one
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(FileName);
            _isUpdatingFromCode = true;
            FileName = nameWithoutExtension + selectedFileType.Extension;
            _isUpdatingFromCode = false;
        }
        else
        {
            // Add the extension
            _isUpdatingFromCode = true;
            FileName = FileName + selectedFileType.Extension;
            _isUpdatingFromCode = false;
        }
    }

    private void SyncDropdownToExtension()
    {
        var currentExtension = Path.GetExtension(FileName);
        if (string.IsNullOrEmpty(currentExtension))
        {
            return;
        }

        // Check if the typed extension matches any known file type
        var matchingIndex = FileTypes.FindIndex(ft =>
            !string.IsNullOrEmpty(ft.Extension) &&
            ft.Extension.Equals(currentExtension, StringComparison.OrdinalIgnoreCase));

        if (matchingIndex >= 0 && matchingIndex != SelectedFileTypeIndex)
        {
            // User typed an extension that matches a known type - switch to it
            _isUpdatingFromCode = true;
            SelectedFileTypeIndex = matchingIndex;
            _isUpdatingFromCode = false;
        }
        else if (matchingIndex < 0 && SelectedFileTypeIndex != OtherFileTypeIndex)
        {
            // User typed an unknown extension - switch to "Other"
            _isUpdatingFromCode = true;
            SelectedFileTypeIndex = OtherFileTypeIndex;
            _isUpdatingFromCode = false;
        }
    }

    private void UpdateValidationState()
    {
        if (Validator is null)
        {
            IsFileNameValid = true;
            IsSubmitEnabled = !string.IsNullOrEmpty(FileName);
            ErrorText = string.Empty;
            return;
        }

        var result = Validator.Validate(FileName);

        IsFileNameValid = result.IsValid;
        IsSubmitEnabled = IsFileNameValid && !string.IsNullOrEmpty(FileName);

        if (result.Errors.Count == 0)
        {
            ErrorText = string.Empty;
        }
        else
        {
            ErrorText = result.Errors[0];
        }
    }

    public void SetDefaultFileName(string defaultFileName, Range selectionRange)
    {
        // The selection range is handled in the dialog itself
        // Use _isUpdatingFromCode to prevent SyncDropdownToExtension from 
        // overriding the saved file type preference during initial setup
        _isUpdatingFromCode = true;
        FileName = defaultFileName;
        _isUpdatingFromCode = false;
    }

    /// <summary>
    /// Called when the file is successfully created to save the extension for next time.
    /// </summary>
    public void SaveFileExtensionPreference()
    {
        var extension = Path.GetExtension(FileName);
        if (!string.IsNullOrEmpty(extension))
        {
            _editorSettings.PreviousNewFileExtension = extension;
        }
    }
}
