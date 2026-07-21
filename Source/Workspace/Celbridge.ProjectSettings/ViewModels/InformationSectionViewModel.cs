using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Information section: the read-only Celbridge version plus the user-editable project version
/// and ignore file. Editing the project version or ignore file writes straight through to the .celbridge
/// file.
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
    private string _ignoreFileText = string.Empty;

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
        IgnoreFileText = config.Resources.IgnoreFile;
        _suppressCommit = false;
    }

    partial void OnProjectVersionTextChanged(string value)
    {
        if (!_suppressCommit)
        {
            WriteEdits(new SetProjectVersionEdit(value));
        }
    }

    partial void OnIgnoreFileTextChanged(string value)
    {
        if (!_suppressCommit)
        {
            WriteEdits(new SetIgnoreFileEdit(value));
        }
    }
}
