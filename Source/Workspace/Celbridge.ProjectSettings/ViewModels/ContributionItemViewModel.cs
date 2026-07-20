using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// One discovered contribution row under its package on the Packages section of Project Settings, carrying
/// the contribution identity, an enable toggle, and the descriptor form fields.
/// </summary>
public partial class ContributionItemViewModel : ObservableObject
{
    private readonly Action<ContributionItemViewModel, bool> _setEnabled;

    private bool _initialized;

    [ObservableProperty]
    private bool _isEnabled;

    public ContributionItemViewModel(
        string packageName,
        string contributionId,
        string displayName,
        bool isUtility,
        bool isOptional,
        bool canToggle,
        string editorId,
        IReadOnlyList<FileTypeInfo> fileTypes,
        Action<ContributionItemViewModel, bool> setEnabled)
    {
        PackageName = packageName;
        ContributionId = contributionId;
        DisplayName = displayName;
        IsUtility = isUtility;
        IsOptional = isOptional;
        CanToggle = canToggle;
        EditorId = editorId;
        FileTypes = fileTypes;
        _setEnabled = setEnabled;
    }

    public string PackageName { get; }
    public string ContributionId { get; }
    public string DisplayName { get; }
    public bool IsUtility { get; }

    public string ToggleTooltip => ProjectSettingsLabels.ContributionToggleTooltip;

    /// <summary>
    /// Whether the contribution is opt-in. Governs whether the enable toggle writes an enabled marker
    /// (optional) or a disabled marker (recommended).
    /// </summary>
    public bool IsOptional { get; }

    /// <summary>
    /// Whether the project may turn this contribution on or off. False for a required contribution,
    /// which is always active and shows no toggle.
    /// </summary>
    public bool CanToggle { get; }

    /// <summary>
    /// The editor id ("{package}.{contribution}") this contribution registers under, used to match the
    /// contribution against the editor-associations map.
    /// </summary>
    public string EditorId { get; }

    public IReadOnlyList<FileTypeInfo> FileTypes { get; }

    public ObservableCollection<ConfigFieldViewModel> ConfigFields { get; } = new();

    /// <summary>
    /// Whether the descriptor form has any fields to show.
    /// </summary>
    public bool HasConfigFields => ConfigFields.Count > 0;

    /// <summary>
    /// Sets the initial enabled value without triggering a write, then enables commits.
    /// </summary>
    public void InitializeState(bool isEnabled)
    {
        IsEnabled = isEnabled;
        _initialized = true;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_initialized)
        {
            _setEnabled(this, value);
        }
    }
}
