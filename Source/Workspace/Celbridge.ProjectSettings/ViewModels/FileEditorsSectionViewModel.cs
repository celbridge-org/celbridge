using System.Collections.ObjectModel;
using Celbridge.Packages;
using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the File Editors section: every extension an active document editor supports, the editors that
/// can open it, and the categories it belongs to. The candidate editors and default come from the runtime
/// resolver, so the section reflects what actually opens a file. Choosing a non-default editor writes an
/// editor association; choosing the default clears it.
/// </summary>
public partial class FileEditorsSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IFileTypeCatalog _fileTypeCatalog;
    private readonly IStringLocalizer _stringLocalizer;

    private bool _suppressFileTypeRebuild;

    // Coalesces filter-box keystrokes so the visible-row list rebuilds once typing pauses, rather than
    // on every character. Null when no UI dispatcher is available (e.g. under test), where the filter
    // rebuilds synchronously instead.
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _filterDebounceTimer;

    private readonly List<(IReadOnlyList<FileTypeCategory> Categories, FileTypeRowViewModel Row)> _allFileTypeRows = new();

    [ObservableProperty]
    private FileTypeCategoryOption? _selectedCategoryOption;

    [ObservableProperty]
    private string _fileTypeFilterText = string.Empty;

    public ObservableCollection<FileTypeRowViewModel> FileTypeRows { get; } = new();
    public ObservableCollection<FileTypeCategoryOption> CategoryOptions { get; } = new();

    /// <summary>
    /// Whether the All category is selected, which is the only view where the extension filter box is
    /// shown (a specific category is short enough not to need it).
    /// </summary>
    public bool IsAllCategorySelected => SelectedCategoryOption?.Category is null;

    public FileEditorsSectionViewModel(
        ProjectSettingsContext context,
        IFileTypeCatalog fileTypeCatalog,
        IStringLocalizer stringLocalizer)
        : base(context)
    {
        _fileTypeCatalog = fileTypeCatalog;
        _stringLocalizer = stringLocalizer;

        var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is not null)
        {
            _filterDebounceTimer = dispatcherQueue.CreateTimer();
            _filterDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
            _filterDebounceTimer.IsRepeating = false;
            _filterDebounceTimer.Tick += (_, _) => RebuildVisibleRows();
        }
    }

    public override void Load()
    {
        BuildFileTypes();
    }

    partial void OnSelectedCategoryOptionChanged(FileTypeCategoryOption? value)
    {
        OnPropertyChanged(nameof(IsAllCategorySelected));
        if (!_suppressFileTypeRebuild)
        {
            RebuildVisibleRows();
        }
    }

    partial void OnFileTypeFilterTextChanged(string value)
    {
        if (_suppressFileTypeRebuild)
        {
            return;
        }

        if (_filterDebounceTimer is null)
        {
            RebuildVisibleRows();
            return;
        }

        // Restart the timer on each keystroke so the list rebuilds once, after a brief pause.
        _filterDebounceTimer.Stop();
        _filterDebounceTimer.Start();
    }

    // Builds every extension that an active document editor (contribution or built-in) supports, with the
    // editors that can open it and the categories it belongs to. The candidate editors and default come
    // from the runtime resolver, so this section matches what actually opens a file (reload state), rather
    // than re-deriving resolution from the manifests.
    private void BuildFileTypes()
    {
        _allFileTypeRows.Clear();

        var packageService = WorkspaceService?.PackageService;
        var documentsService = WorkspaceService?.DocumentsService;
        var config = GetConfig();
        if (packageService is null
            || documentsService is null
            || config is null)
        {
            CategoryOptions.Clear();
            FileTypeRows.Clear();
            return;
        }

        // Category and provenance come from the active document editors: a package or project instance
        // contributes a custom format, a built-in a standard one.
        var manifestCategoryByExtension = new Dictionary<string, FileTypeCategory?>(StringComparer.OrdinalIgnoreCase);
        var packageContributedByExtension = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in packageService.GetResolvedEditors())
        {
            if (instance.Contribution.IsUtility)
            {
                continue;
            }
            RecordFileTypeProvenance(instance.Contribution.FileTypes, isPackage: true, manifestCategoryByExtension, packageContributedByExtension);
        }
        foreach (var builtIn in packageService.GetBuiltInEditors())
        {
            RecordFileTypeProvenance(builtIn.Contribution.FileTypes, isPackage: false, manifestCategoryByExtension, packageContributedByExtension);
        }

        foreach (var extension in manifestCategoryByExtension.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            var pick = documentsService.GetEditorCandidatesForExtension(extension);
            if (pick.Candidates.Count == 0)
            {
                // No registered editor opens this extension, so there is nothing to pin; skip it.
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

            var isPackageContributed = packageContributedByExtension.GetValueOrDefault(extension);
            var categories = ResolveCategories(extension, manifestCategoryByExtension[extension], isPackageContributed);
            config.Celbridge.EditorAssociations.TryGetValue(extension, out var associatedEditorId);

            var typeName = _fileTypeCatalog.GetDisplayName(extension);
            var row = new FileTypeRowViewModel(extension, typeName, candidates, defaultEditorId, associatedEditorId, CommitAssociation);
            _allFileTypeRows.Add((categories, row));
        }

        BuildCategoryOptions();
        RebuildVisibleRows();
    }

    // Records the category and package provenance for each extension a document editor supports. A
    // declared category wins over an undeclared (null) one when editors disagree; provenance is set
    // once a package editor claims the extension.
    private static void RecordFileTypeProvenance(
        IReadOnlyList<EditorFileType> fileTypes,
        bool isPackage,
        Dictionary<string, FileTypeCategory?> manifestCategoryByExtension,
        Dictionary<string, bool> packageContributedByExtension)
    {
        foreach (var fileType in fileTypes)
        {
            var extension = fileType.FileExtension.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                continue;
            }

            if (!manifestCategoryByExtension.TryGetValue(extension, out var existingCategory))
            {
                manifestCategoryByExtension[extension] = fileType.Category;
            }
            else if (existingCategory is null
                && fileType.Category is not null)
            {
                manifestCategoryByExtension[extension] = fileType.Category;
            }

            if (isPackage)
            {
                packageContributedByExtension[extension] = true;
            }
            else
            {
                packageContributedByExtension.TryAdd(extension, false);
            }
        }
    }

    // An extension's categories: the central catalog wins for established types, else a package-declared
    // manifest category; a package-contributed format also gets the Custom category; the code editor's
    // long tail of uncatalogued text formats falls back to Text.
    private IReadOnlyList<FileTypeCategory> ResolveCategories(string extension, FileTypeCategory? manifestCategory, bool isPackageContributed)
    {
        var categories = new List<FileTypeCategory>();

        var catalogCategories = _fileTypeCatalog.GetCategories(extension);
        if (catalogCategories.Count > 0)
        {
            categories.AddRange(catalogCategories);
        }
        else if (manifestCategory is not null)
        {
            categories.Add(manifestCategory.Value);
        }

        if (isPackageContributed)
        {
            categories.Add(FileTypeCategory.Custom);
        }

        if (categories.Count == 0)
        {
            categories.Add(FileTypeCategory.Text);
        }

        return categories;
    }

    private void BuildCategoryOptions()
    {
        _suppressFileTypeRebuild = true;

        var hadSelection = SelectedCategoryOption is not null;
        var previousCategory = SelectedCategoryOption?.Category;

        CategoryOptions.Clear();

        // "All" is the show-everything option, so it stays first; the real categories follow in
        // alphabetical order by their displayed label.
        CategoryOptions.Add(new FileTypeCategoryOption(CategoryLabel(null), null));

        var presentCategories = _allFileTypeRows.SelectMany(entry => entry.Categories).ToHashSet();
        var orderedCategories = presentCategories
            .Select(category => new FileTypeCategoryOption(CategoryLabel(category), category))
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase);
        foreach (var option in orderedCategories)
        {
            CategoryOptions.Add(option);
        }

        FileTypeCategoryOption? target;
        if (hadSelection)
        {
            // Preserve the user's category across a rebuild so it persists until the project unloads.
            target = CategoryOptions.FirstOrDefault(option => option.Category == previousCategory);
        }
        else
        {
            // On first load default to the project's own custom formats, when it has any.
            target = CategoryOptions.FirstOrDefault(option => option.Category == FileTypeCategory.Custom);
        }

        SelectedCategoryOption = target ?? CategoryOptions.FirstOrDefault();

        _suppressFileTypeRebuild = false;
    }

    // Rebuilds the flat, alphabetically-sorted list from the selected category and, in the All view, the
    // filter text. The category picker does the grouping; a file type appears under any category it
    // belongs to. _allFileTypeRows is already in extension order, so the list stays alphabetical.
    private void RebuildVisibleRows()
    {
        FileTypeRows.Clear();

        var selectedCategory = SelectedCategoryOption?.Category;
        var filter = FileTypeFilterText?.Trim() ?? string.Empty;

        // The filter box is only shown for the All view, so it only narrows results there.
        var hasFilter = selectedCategory is null && filter.Length > 0;

        foreach (var entry in _allFileTypeRows)
        {
            if (selectedCategory is not null
                && !entry.Categories.Contains(selectedCategory.Value))
            {
                continue;
            }

            if (hasFilter
                && !entry.Row.Extension.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FileTypeRows.Add(entry.Row);
        }
    }

    private string CategoryLabel(FileTypeCategory? category)
    {
        var name = category?.ToString() ?? "All";
        return _stringLocalizer.GetString($"ProjectSettings_Category_{name}");
    }

    private void CommitAssociation(string extension, string? editorId)
    {
        ProjectConfigEdit edit;
        if (editorId is null)
        {
            edit = new RemoveEditorAssociationEdit(extension);
        }
        else
        {
            edit = new SetEditorAssociationEdit(extension, editorId);
        }

        WriteEdits(edit);
    }
}
