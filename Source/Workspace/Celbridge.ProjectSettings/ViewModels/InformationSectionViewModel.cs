using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Information section: the read-only Celbridge version plus the user-editable project version
/// and description. Edits are written to the .celbridge file and apply when the project is reloaded.
/// </summary>
public partial class InformationSectionViewModel : ProjectSettingsSectionViewModel
{
    // Commits are suppressed while Load populates the fields from disk, so building them does not write
    // them back.
    private bool _suppressCommit;

    [ObservableProperty]
    private string _schemaVersionText = string.Empty;

    [ObservableProperty]
    private string _projectVersionText = string.Empty;

    [ObservableProperty]
    private string _descriptionText = string.Empty;

    public InformationSectionViewModel(ProjectSettingsContext context)
        : base(context)
    {
    }

    public override void Load()
    {
        var config = GetConfig();
        if (config is null)
        {
            return;
        }

        _suppressCommit = true;
        SchemaVersionText = config.Celbridge.CelbridgeVersion ?? string.Empty;
        ProjectVersionText = config.Celbridge.ProjectVersion ?? string.Empty;
        DescriptionText = config.Celbridge.Description ?? string.Empty;
        _suppressCommit = false;
    }

    partial void OnProjectVersionTextChanged(string value)
    {
        if (!_suppressCommit)
        {
            EditConfig(draft => draft.ProjectVersion = value);
        }
    }

    partial void OnDescriptionTextChanged(string value)
    {
        if (!_suppressCommit)
        {
            EditConfig(draft => draft.Description = value);
        }
    }
}
