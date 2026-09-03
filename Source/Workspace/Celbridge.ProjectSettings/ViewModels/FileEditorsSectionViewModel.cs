using System.Collections.ObjectModel;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Projects;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the File Editors section: the file types that more than one active document editor can open,
/// paired with the editor that opens each. The candidate editors and default come from the runtime
/// resolver, so the section reflects what actually opens a file. Choosing a non-default editor writes an
/// editor association; choosing the default clears it. A file type only one editor claims presents no
/// choice, so it is not listed.
/// </summary>
public class FileEditorsSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IFileTypeCatalog _fileTypeCatalog;
    private readonly ILogger<FileEditorsSectionViewModel> _logger;

    public ObservableCollection<FileTypeRowViewModel> FileTypeRows { get; } = new();

    public FileEditorsSectionViewModel(
        ProjectSettingsContext context,
        IFileTypeCatalog fileTypeCatalog)
        : base(context)
    {
        _fileTypeCatalog = fileTypeCatalog;
        _logger = ServiceLocator.AcquireService<ILogger<FileEditorsSectionViewModel>>();
    }

    public bool HasFileTypes => FileTypeRows.Count > 0;

    public bool HasNoFileTypes => FileTypeRows.Count == 0;

    public string EmptyText => ProjectSettingsLabels.FileEditorsEmpty;

    public override void Load()
    {
        BuildFileTypes();
    }

    // Builds a row for each extension that more than one active document editor (contribution or built-in)
    // claims. The candidate editors and default come from the runtime resolver, so this section matches what
    // actually opens a file (reload state), rather than re-deriving resolution from the manifests.
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
            NotifyFileTypesChanged();
            return;
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in packageService.GetResolvedEditors())
        {
            if (instance.Contribution.IsUtility)
            {
                continue;
            }
            CollectExtensions(instance.Contribution.FileTypes, extensions);
        }
        foreach (var builtIn in packageService.GetBuiltInEditors())
        {
            CollectExtensions(builtIn.Contribution.FileTypes, extensions);
        }

        foreach (var extension in extensions.OrderBy(key => key, StringComparer.Ordinal))
        {
            if (documentsService.IsReservedFileType(extension))
            {
                // Core file types carry a role the application depends on, so they are not the user's
                // to reassign, even when the code editor also claims them as text.
                continue;
            }

            var pick = documentsService.GetEditorCandidatesForExtension(extension);
            if (pick.Candidates.Count < 2)
            {
                // Only one editor opens this extension, so there is nothing to choose between.
                continue;
            }

            var defaultEditorId = pick.DefaultEditorId.ToString();

            // List the default editor first, then the rest alphabetically, so the dropdown reads
            // predictably rather than in internal editor-resolution order.
            var candidates = pick.Candidates
                .Select(candidate => new AssociationCandidate(candidate.EditorId.ToString(), candidate.DisplayName))
                .OrderByDescending(candidate => candidate.EditorId == defaultEditorId)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            config.Celbridge.EditorAssociations.TryGetValue(extension, out var associatedEditorId);

            var typeName = _fileTypeCatalog.GetDisplayName(extension);
            var row = new FileTypeRowViewModel(extension, typeName, candidates, defaultEditorId, associatedEditorId, CommitAssociation);
            FileTypeRows.Add(row);
        }

        NotifyFileTypesChanged();
    }

    // Records each extension a document editor supports, lowercased so the same extension claimed by two
    // editors resolves to one row.
    private static void CollectExtensions(IReadOnlyList<EditorFileType> fileTypes, HashSet<string> extensions)
    {
        foreach (var fileType in fileTypes)
        {
            var extension = fileType.FileExtension.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                continue;
            }

            extensions.Add(extension);
        }
    }

    private void NotifyFileTypesChanged()
    {
        OnPropertyChanged(nameof(HasFileTypes));
        OnPropertyChanged(nameof(HasNoFileTypes));
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
