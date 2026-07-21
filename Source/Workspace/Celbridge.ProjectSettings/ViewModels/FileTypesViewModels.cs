using Celbridge.Packages;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// An extension a document editor handles, paired with the category it groups under in the File Editors
/// section, or null when the manifest declares no category.
/// </summary>
public sealed record FileTypeInfo(string Extension, FileTypeCategory? Category);

/// <summary>
/// A candidate editor for an extension, pairing the editor id written to the associations map with the
/// display name shown in the dropdown.
/// </summary>
public sealed record AssociationCandidate(string EditorId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// A category choice in the File Editors section picker: a specific category, or All when Category is null.
/// </summary>
public sealed record FileTypeCategoryOption(string Label, FileTypeCategory? Category)
{
    public override string ToString() => Label;
}

/// <summary>
/// One extension in the File Editors section. When more than one editor claims the extension a dropdown pins
/// which one opens it (writing editor-associations, clearing the entry when the resolution default is
/// chosen); otherwise the single editor is shown as read-only text.
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
        IReadOnlyList<AssociationCandidate> candidates,
        string defaultEditorId,
        string? associatedEditorId,
        Action<string, string?> commit)
    {
        Extension = extension;
        Candidates = candidates;
        _defaultEditorId = defaultEditorId;
        _commit = commit;

        var effectiveEditorId = associatedEditorId ?? defaultEditorId;
        SelectedCandidate = candidates.FirstOrDefault(candidate => candidate.EditorId == effectiveEditorId)
            ?? candidates.FirstOrDefault();

        _initialized = true;
    }

    public string Extension { get; }
    public IReadOnlyList<AssociationCandidate> Candidates { get; }

    public string EditorPickerTooltip => ProjectSettingsLabels.EditorPickerTooltip;

    /// <summary>
    /// Whether more than one editor claims this extension, so a dropdown is shown to pin the choice.
    /// </summary>
    public bool IsContested => Candidates.Count > 1;

    /// <summary>
    /// Whether exactly one editor claims this extension, so a read-only editor name is shown instead of
    /// a dropdown.
    /// </summary>
    public bool IsSingleEditor => Candidates.Count == 1;

    /// <summary>
    /// Display name of the currently selected editor, shown when the extension is not contested.
    /// </summary>
    public string EditorName => SelectedCandidate?.DisplayName ?? string.Empty;

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
