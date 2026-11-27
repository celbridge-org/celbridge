using Celbridge.Validators;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// The name field of the resource picker's save mode, which names the file to save in the current folder. The field
/// checks the typed name against the folder's listing, and shows why a name can't be used.
/// </summary>
public partial class ResourcePickerNameField : ObservableObject
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IResourceNameValidatorFactory _nameValidatorFactory;

    private ResourcePickerExtensions _extensions = ResourcePickerExtensions.Any;
    // Checks the typed name against the current folder's listing. Null until the field has a folder.
    private IValidator? _nameValidator;
    // Every child of the current folder, including the ones the picker leaves out. The field finds an existing file
    // here, with the name stored on disk.
    private IReadOnlyList<FolderChild> _folderChildren = [];

    /// <summary>
    /// Raised when the picker puts a name in the field, for example when the user clicks a file. The view then selects
    /// the name without its extension.
    /// </summary>
    public event EventHandler? NameSuggested;

    [ObservableProperty]
    private Visibility _visibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _isErrorVisible;

    internal ResourcePickerNameField(
        IStringLocalizer stringLocalizer,
        IResourceNameValidatorFactory nameValidatorFactory)
    {
        _stringLocalizer = stringLocalizer;
        _nameValidatorFactory = nameValidatorFactory;
    }

    /// <summary>
    /// The name of the file to save, with the letter case stored on disk when the file exists. Null while the typed
    /// name can't be used.
    /// </summary>
    public string? SaveName { get; private set; }

    /// <summary>
    /// Returns true when a file in the current folder already has the save name.
    /// </summary>
    public bool IsSaveNameTaken { get; private set; }

    /// <summary>
    /// Sets the field up for a picker, and empties it. The field shows only in save mode.
    /// </summary>
    internal void Reset(ResourcePickerExtensions extensions, bool isSaveMode)
    {
        _extensions = extensions;
        _nameValidator = null;
        _folderChildren = [];
        Visibility = isSaveMode
            ? Visibility.Visible
            : Visibility.Collapsed;

        Text = string.Empty;
        UpdateState();
    }

    /// <summary>
    /// Checks the name against a newly listed folder from now on.
    /// </summary>
    internal void SetFolder(ResourceKey folder, IReadOnlyList<FolderChild> folderChildren)
    {
        _nameValidator = _nameValidatorFactory.CreateFromListing(
            folder,
            folderChildren,
            ResourceNameKind.FileToSave);
        _folderChildren = folderChildren;

        UpdateState();
    }

    /// <summary>
    /// Puts a name in the field, and asks the view to select the name without its extension.
    /// </summary>
    internal void Suggest(string name)
    {
        Text = name;
        NameSuggested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Checks the name of a file to save in a folder, against a listing of that folder. Returns why the name can't be
    /// used, or null when the name can be used.
    /// </summary>
    internal string? CheckName(ResourceKey folder, IReadOnlyList<FolderChild> folderChildren, string name)
    {
        var nameValidator = _nameValidatorFactory.CreateFromListing(
            folder,
            folderChildren,
            ResourceNameKind.FileToSave);

        return GetNameError(nameValidator, name);
    }

    /// <summary>
    /// Returns the name stored on disk when a file in the folder has the name in another letter case. A key names an
    /// existing file only by its stored name.
    /// </summary>
    internal static string GetStoredName(IReadOnlyList<FolderChild> folderChildren, string name)
    {
        var existingFile = folderChildren.FirstOrDefault(child =>
            !child.IsFolder
            && child.Resource.ResourceName.Equals(name, StringComparison.OrdinalIgnoreCase));

        return existingFile?.Resource.ResourceName ?? name;
    }

    partial void OnTextChanged(string value)
    {
        UpdateState();
    }

    // Checks the typed name, and sets the save name. The name is checked only once the field has a folder.
    private void UpdateState()
    {
        SaveName = null;
        IsSaveNameTaken = false;

        if (_nameValidator is null)
        {
            ShowError(string.Empty);
            return;
        }

        var name = _extensions.AddSingleExtension(Text);

        var nameError = GetNameError(_nameValidator, name);
        if (nameError is not null)
        {
            // An empty field disables Save without an error. The field starts empty when there is no suggested name.
            var shownError = string.IsNullOrEmpty(Text)
                ? string.Empty
                : nameError;
            ShowError(shownError);
            return;
        }

        var saveName = GetStoredName(_folderChildren, name);
        SaveName = saveName;
        IsSaveNameTaken = _folderChildren.Any(child =>
            !child.IsFolder
            && child.Resource.ResourceName == saveName);

        ShowError(string.Empty);
    }

    // Returns why the name of a file to save can't be used, or null when the name can be used. The validator checks
    // the name against the listing of the file's folder.
    private string? GetNameError(IValidator nameValidator, string name)
    {
        var validationResult = nameValidator.Validate(name);
        if (!validationResult.IsValid)
        {
            return validationResult.Errors.FirstOrDefault() ?? string.Empty;
        }

        if (!_extensions.IsAllowed(name))
        {
            return _stringLocalizer.GetString("ResourcePickerDialog_ExtensionRequired", _extensions.ListText);
        }

        return null;
    }

    private void ShowError(string error)
    {
        ErrorText = error;
        IsErrorVisible = !string.IsNullOrEmpty(error);
    }
}
