using Celbridge.Projects;

namespace Celbridge.UserInterface.ViewModels;

/// <summary>
/// Holds the state of the Download File dialog: the file, the site offering the file and the download folder.
/// The view model also says which of the dialog's two pages shows.
/// </summary>
public partial class DownloadFileDialogViewModel : ObservableObject
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private Visibility _sourceVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private ResourceKey _downloadFolder = ResourceKey.Empty;

    [ObservableProperty]
    private string _downloadFolderText = string.Empty;

    [ObservableProperty]
    private bool _isChoosingFolder;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _primaryButtonText = string.Empty;

    [ObservableProperty]
    private string _secondaryButtonText = string.Empty;

    [ObservableProperty]
    private Visibility _filePageVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility _folderPageVisibility = Visibility.Collapsed;

    public DownloadFileDialogViewModel(
        IStringLocalizer stringLocalizer,
        IProjectService projectService)
    {
        _stringLocalizer = stringLocalizer;
        _projectService = projectService;
    }

    /// <summary>
    /// Sets the dialog up for a file downloaded from the source URL, with the given download folder, showing the
    /// first page.
    /// </summary>
    public void Initialize(string fileName, string sourceUrl, ResourceKey downloadFolder)
    {
        FileName = fileName;

        var siteName = GetSiteName(sourceUrl);
        if (string.IsNullOrEmpty(siteName))
        {
            SourceText = string.Empty;
            SourceVisibility = Visibility.Collapsed;
        }
        else
        {
            SourceText = _stringLocalizer.GetString("DownloadFileDialog_Source", siteName);
            SourceVisibility = Visibility.Visible;
        }

        SetDownloadFolder(downloadFolder);
        ShowFilePage();
    }

    /// <summary>
    /// Makes the folder the download folder. An empty key is the project folder itself.
    /// </summary>
    public void SetDownloadFolder(ResourceKey folder)
    {
        DownloadFolder = folder;

        if (folder.IsEmpty)
        {
            var projectName = _projectService.CurrentProject?.ProjectName ?? string.Empty;
            DownloadFolderText = _stringLocalizer.GetString("ResourcePickerDialog_ProjectFolder", projectName);
        }
        else
        {
            DownloadFolderText = folder.Path;
        }
    }

    /// <summary>
    /// Shows the first page, which saves the file to the download folder.
    /// </summary>
    public void ShowFilePage()
    {
        IsChoosingFolder = false;
        TitleText = _stringLocalizer.GetString("DownloadFileDialog_Title");
        PrimaryButtonText = _stringLocalizer.GetString("DialogButton_Save");
        SecondaryButtonText = _stringLocalizer.GetString("DialogButton_Cancel");
        FilePageVisibility = Visibility.Visible;
        FolderPageVisibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows the second page, which chooses the download folder.
    /// </summary>
    public void ShowFolderPage()
    {
        IsChoosingFolder = true;
        TitleText = _stringLocalizer.GetString("DownloadFileDialog_SelectFolderTitle");
        PrimaryButtonText = _stringLocalizer.GetString("DialogButton_Select");
        SecondaryButtonText = _stringLocalizer.GetString("DialogButton_Back");
        FilePageVisibility = Visibility.Collapsed;
        FolderPageVisibility = Visibility.Visible;
    }

    /// <summary>
    /// Returns the host name of the site a file is downloaded from, or an empty string when the address names no
    /// host. A blob: address gives the host of the page that made the blob.
    /// </summary>
    public static string GetSiteName(string sourceUrl)
    {
        const string BlobPrefix = "blob:";

        var address = sourceUrl;
        if (address.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase))
        {
            address = address.Substring(BlobPrefix.Length);
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return uri.Host;
    }
}
