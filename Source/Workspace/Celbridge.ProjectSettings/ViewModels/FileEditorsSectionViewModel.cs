using System.Collections.ObjectModel;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Projects;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the File Editors section: the file types that offer a choice of editor, paired with the editor
/// that opens each. The candidate editors and default come from the runtime resolver, so the section
/// reflects what actually opens a file. Choosing a non-default editor writes an editor association;
/// choosing the default clears it. A file type only one editor claims presents no choice, so it is not
/// listed unless it carries a stale association the user would otherwise be unable to clear.
/// </summary>
public class FileEditorsSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IFileTypeCatalog _fileTypeCatalog;
    private readonly ILogger<FileEditorsSectionViewModel> _logger;

    public ObservableCollection<FileTypeRowViewModel> FileTypeRows { get; } = new();

    public override string EmptyText => ProjectSettingsLabels.FileEditorsEmpty;

    public FileEditorsSectionViewModel(
        ProjectSettingsContext context,
        IFileTypeCatalog fileTypeCatalog)
        : base(context)
    {
        _fileTypeCatalog = fileTypeCatalog;
        _logger = ServiceLocator.AcquireService<ILogger<FileEditorsSectionViewModel>>();
    }

    public override void Load()
    {
        BuildFileTypes();
    }

    // Builds a row for each extension that offers a choice of editor. The candidate editors and default come
    // from the runtime resolver, so this section matches what actually opens a file (reload state), rather
    // than re-deriving resolution from the manifests.
    private void BuildFileTypes()
    {
        FileTypeRows.Clear();

        var packageService = WorkspaceService?.PackageService;
        var documentsService = WorkspaceService?.DocumentsService;
        var config = GetConfig();
        if (packageService is null
            || documentsService is null
            || config is null)
        {
            SetNotLoadedContentState();
            return;
        }

        // Extensions are lowercased as they are collected, so an ordinal set and ordering are enough.
        var extensions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var instance in packageService.GetResolvedEditors())
        {
            CollectExtensions(instance.Contribution, extensions);
        }
        foreach (var builtIn in packageService.GetBuiltInEditors())
        {
            CollectExtensions(builtIn.Contribution, extensions);
        }

        foreach (var extension in extensions.OrderBy(key => key, StringComparer.Ordinal))
        {
            var pick = documentsService.GetEditorCandidatesForExtension(extension);
            var defaultEditorId = pick.DefaultEditorId.ToString();

            config.Celbridge.EditorAssociations.TryGetValue(extension, out var associatedEditorId);

            var candidates = BuildCandidates(pick, defaultEditorId, associatedEditorId);
            if (candidates.Count < 2)
            {
                // Only one editor opens this extension, so there is nothing to choose between.
                continue;
            }

            if (documentsService.IsReservedFileType(extension))
            {
                // Core file types carry a role the application depends on, so they are not the user's
                // to reassign, even when the code editor also claims them as text.
                continue;
            }

            var typeName = _fileTypeCatalog.GetDisplayName(extension);
            var row = new FileTypeRowViewModel(extension, typeName, candidates, defaultEditorId, associatedEditorId, CommitAssociation);
            FileTypeRows.Add(row);
        }

        SetLoadedContentState(FileTypeRows.Count);
    }

    // The editors a dropdown offers for an extension: the default first, then the rest alphabetically, so
    // the list reads predictably rather than in internal editor-resolution order. An association naming an
    // editor that no longer claims the extension is stale and opens nothing, so it is listed last, letting
    // the user pick a working editor and clear the entry.
    private static List<AssociationCandidate> BuildCandidates(
        ExtensionEditorCandidates pick,
        string defaultEditorId,
        string? associatedEditorId)
    {
        var candidates = pick.Candidates
            .Select(candidate => new AssociationCandidate(candidate.EditorId.ToString(), candidate.DisplayName))
            .OrderByDescending(candidate => candidate.EditorId == defaultEditorId)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (associatedEditorId is not null
            && !candidates.Any(candidate => candidate.EditorId == associatedEditorId))
        {
            var unavailableName = ProjectSettingsLabels.UnavailableEditor(associatedEditorId);
            candidates.Add(new AssociationCandidate(associatedEditorId, unavailableName));
        }

        return candidates;
    }

    // Records each extension a document editor supports, lowercased so the same extension claimed by two
    // editors resolves to one row. A utility is presented by the Utility Panel rather than opened by
    // extension, so it contributes none.
    private static void CollectExtensions(EditorContribution contribution, HashSet<string> extensions)
    {
        if (contribution.IsUtility)
        {
            return;
        }

        foreach (var fileType in contribution.FileTypes)
        {
            var extension = fileType.FileExtension.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                continue;
            }

            extensions.Add(extension);
        }
    }

    private void CommitAssociation(string extension, string? editorId)
    {
        if (editorId is null)
        {
            EditConfig(draft => draft.RemoveEditorAssociation(extension));
            return;
        }

        // An extension the catalog reported but the draft rejects is a malformed claim rather than
        // anything the user did, so the pick is dropped and the claim is logged.
        var editResult = EditConfig(draft => draft.SetEditorAssociation(extension, editorId));
        if (editResult.IsFailure)
        {
            _logger.LogError(editResult, $"Failed to associate file extension '{extension}' with editor '{editorId}'");
        }
    }
}
