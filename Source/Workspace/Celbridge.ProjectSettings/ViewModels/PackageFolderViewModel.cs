using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The reasons that loading the project drops a listed package folder.
/// </summary>
public enum PackageFolderProblem
{
    None,
    ProjectRoot,
    InvalidPath,
    AlreadyListed,
}

/// <summary>
/// A row of the Resources section for one package folder that the project lists. The row shows the folder's
/// path, and any problem that would make loading the project drop the folder.
/// </summary>
public partial class PackageFolderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    private PackageFolderProblem _problem;

    public bool HasProblem => Problem != PackageFolderProblem.None;

    /// <summary>
    /// The message beneath the row while HasProblem is true.
    /// </summary>
    public string ProblemText
    {
        get
        {
            switch (Problem)
            {
                case PackageFolderProblem.ProjectRoot:
                    return ProjectSettingsLabels.ProjectRootPackageFolder;
                case PackageFolderProblem.InvalidPath:
                    return ProjectSettingsLabels.InvalidPackageFolder;
                case PackageFolderProblem.AlreadyListed:
                    return ProjectSettingsLabels.RepeatedPackageFolder;
                default:
                    return string.Empty;
            }
        }
    }

    public string FolderLabel => ProjectSettingsLabels.PackageFoldersTitle;
    public string BrowseTooltip => ProjectSettingsLabels.PackageFolderBrowseTooltip;
    public string RemoveTooltip => ProjectSettingsLabels.RemovePackageFolderTooltip;
}
