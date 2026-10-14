using Celbridge.Documents;
using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Information section: the read-only Celbridge version, the user-editable project version
/// and description, and the buttons that open the project's data folder and load report.
/// </summary>
public partial class InformationSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IProjectLoadReporter _projectLoadReporter;

    // Commits are suppressed while Load populates the fields from disk, so building them does not write
    // them back.
    private bool _suppressCommit;

    [ObservableProperty]
    private string _celbridgeVersionText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectVersionInvalid))]
    private string _projectVersionText = string.Empty;

    [ObservableProperty]
    private string _descriptionText = string.Empty;

    /// <summary>
    /// True when the project version box holds text that is not a three-part version, which is not written
    /// to the project file. An empty box is valid, and leaves the project at the default version.
    /// </summary>
    public bool IsProjectVersionInvalid => SemanticVersion.ParseOptional(ProjectVersionText.Trim()).IsFailure;

    /// <summary>
    /// The version of a project that sets none, shown in the project version box while it is empty.
    /// </summary>
    public string ProjectVersionPlaceholder => SemanticVersion.Default.ToString();

    public InformationSectionViewModel(
        ProjectSettingsContext context,
        IProjectLoadReporter projectLoadReporter)
        : base(context)
    {
        _projectLoadReporter = projectLoadReporter;
    }

    public override void Load()
    {
        var config = GetConfig();
        if (config is null)
        {
            return;
        }

        _suppressCommit = true;
        CelbridgeVersionText = config.Celbridge.CelbridgeVersion ?? string.Empty;
        ProjectVersionText = config.Celbridge.ProjectVersion ?? string.Empty;
        DescriptionText = config.Celbridge.Description ?? string.Empty;
        _suppressCommit = false;
    }

    [RelayCommand]
    private void OpenProjectData()
    {
        CommandService.Execute<IOpenProjectDataCommand>();
    }

    [RelayCommand]
    private void OpenLoadReport()
    {
        var loadReport = _projectLoadReporter.WrittenReport;
        if (loadReport is null)
        {
            return;
        }

        CommandService.Execute<IOpenDocumentCommand>(command => command.FileResource = loadReport.Resource);
    }

    partial void OnProjectVersionTextChanged(string value)
    {
        if (_suppressCommit)
        {
            return;
        }

        // The file keeps its last valid version while the box holds text that is not one.
        if (IsProjectVersionInvalid)
        {
            return;
        }

        var projectVersion = value.Trim();
        EditConfig(draft => draft.ProjectVersion = projectVersion);
    }

    partial void OnDescriptionTextChanged(string value)
    {
        if (!_suppressCommit)
        {
            EditConfig(draft => draft.Description = value);
        }
    }
}
