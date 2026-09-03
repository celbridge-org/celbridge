using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// A candidate editor for an extension, pairing the editor id written to the associations map with the
/// display name shown in the dropdown.
/// </summary>
public sealed record AssociationCandidate(string EditorId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// One extension in the File Editors section. Choosing a candidate writes the editor association for the
/// extension, and choosing the resolution default clears it.
/// </summary>
public partial class FileTypeRowViewModel : ObservableObject
{
    private readonly string _defaultEditorId;
    private readonly Action<string, string?> _commit;

    private bool _initialized;

    [ObservableProperty]
    private AssociationCandidate? _selectedCandidate;

    public FileTypeRowViewModel(
        string extension,
        string typeName,
        IReadOnlyList<AssociationCandidate> candidates,
        string defaultEditorId,
        string? associatedEditorId,
        Action<string, string?> commit)
    {
        Extension = extension;
        TypeName = typeName;
        Candidates = candidates;
        _defaultEditorId = defaultEditorId;
        _commit = commit;

        var effectiveEditorId = associatedEditorId ?? defaultEditorId;
        SelectedCandidate = candidates.FirstOrDefault(candidate => candidate.EditorId == effectiveEditorId)
            ?? candidates.FirstOrDefault();

        _initialized = true;
    }

    public string Extension { get; }

    /// <summary>
    /// The name the host catalog knows this file type by, or empty for an extension it does not
    /// catalogue.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Whether the catalog names this file type.
    /// </summary>
    public bool HasTypeName => !string.IsNullOrEmpty(TypeName);

    public IReadOnlyList<AssociationCandidate> Candidates { get; }

    public string EditorPickerTooltip => ProjectSettingsLabels.EditorPickerTooltip;

    partial void OnSelectedCandidateChanged(AssociationCandidate? value)
    {
        if (!_initialized
            || value is null)
        {
            return;
        }

        if (value.EditorId == _defaultEditorId)
        {
            _commit(Extension, null);
        }
        else
        {
            _commit(Extension, value.EditorId);
        }
    }
}
