using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Celbridge.Dialog;
using Celbridge.Patterns;
using Celbridge.ProjectSettings.Helpers;
using Celbridge.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Resources section: fills its fields from the project config and commits every edit back to
/// the draft. A pattern list is edited as one block of text, a pattern per line, so a list can be pasted
/// from one project into another. The package folders are edited as rows, one folder per row.
/// </summary>
public partial class ResourcesSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IDialogService _dialogService;

    // Set while the section rebuilds itself from the config, so filling the fields does not write what it
    // has just read straight back into the draft.
    private bool _suppressCommit;

    /// <summary>
    /// The package folders the project lists in addition to the packages folder.
    /// </summary>
    public ObservableCollection<PackageFolderViewModel> PackageFolders { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalidHidePattern))]
    private string _hidePatternsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalidSearchExcludePattern))]
    private string _searchExcludePatternsText = string.Empty;

    // Empty while the project names no folder of its own, and the placeholder then names the default.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadsFolderInvalid))]
    [NotifyPropertyChangedFor(nameof(InvalidDownloadsFolderText))]
    private string _downloadsFolderText = string.Empty;

    /// <summary>
    /// True when the gitignore matcher refuses a line of the hide list. Loading the project drops that line.
    /// </summary>
    public bool HasInvalidHidePattern => ContainsInvalidPattern(HidePatternsText);

    /// <summary>
    /// True when the gitignore matcher refuses a line of the search-exclude list.
    /// </summary>
    public bool HasInvalidSearchExcludePattern => ContainsInvalidPattern(SearchExcludePatternsText);

    /// <summary>
    /// True when the downloads folder is not a folder path, or is inside a folder Celbridge reserves, which
    /// leaves downloads going to the default folder.
    /// </summary>
    public bool IsDownloadsFolderInvalid =>
        !string.IsNullOrWhiteSpace(DownloadsFolderText)
        && !DownloadsFolderPath.TryParse(DownloadsFolderText, out _);

    /// <summary>
    /// The folder downloads are saved to while the project names no other.
    /// </summary>
    public string DefaultDownloadsFolder => DownloadsFolderPath.DefaultFolder.Path;

    public string HideTitle => ProjectSettingsLabels.HideTitle;
    public string HideSubtitle => ProjectSettingsLabels.HideSubtitle;
    public string SearchExcludeTitle => ProjectSettingsLabels.SearchExcludeTitle;
    public string SearchExcludeSubtitle => ProjectSettingsLabels.SearchExcludeSubtitle;
    public string PatternPlaceholder => ProjectSettingsLabels.PatternPlaceholder;
    public string InvalidPatternText => ProjectSettingsLabels.InvalidPattern;
    public string DownloadsFolderTitle => ProjectSettingsLabels.DownloadsFolderTitle;
    public string DownloadsFolderSubtitle => ProjectSettingsLabels.DownloadsFolderSubtitle;
    public string DownloadsFolderBrowseTooltip => ProjectSettingsLabels.DownloadsFolderBrowseTooltip;
    /// <summary>
    /// Why the downloads folder is not used, said beneath the field while IsDownloadsFolderInvalid is true.
    /// </summary>
    public string InvalidDownloadsFolderText => DownloadsFolderPath.IsReserved(DownloadsFolderText)
        ? ProjectSettingsLabels.ReservedDownloadsFolder
        : ProjectSettingsLabels.InvalidDownloadsFolder;

    public string PackageFoldersTitle => ProjectSettingsLabels.PackageFoldersTitle;
    public string PackageFoldersSubtitle => ProjectSettingsLabels.PackageFoldersSubtitle;
    public string DefaultPackageFolderTooltip => ProjectSettingsLabels.DefaultPackageFolderTooltip;
    public string AddPackageFolderText => ProjectSettingsLabels.AddPackageFolder;

    /// <summary>
    /// The package folder every project has, shown first and fixed.
    /// </summary>
    public string DefaultPackageFolder => PackageFolderPath.DefaultFolder.Path;

    public ResourcesSectionViewModel(
        ProjectSettingsContext context,
        IDialogService dialogService)
        : base(context)
    {
        _dialogService = dialogService;

        PackageFolders.CollectionChanged += PackageFolders_CollectionChanged;
    }

    public override void Load()
    {
        var config = GetConfig();

        _suppressCommit = true;
        try
        {
            HidePatternsText = FormatPatterns(config?.Resources.Hide);
            SearchExcludePatternsText = FormatPatterns(config?.Resources.SearchExclude);
            DownloadsFolderText = config?.Resources.DownloadsFolder ?? string.Empty;

            // Clearing raises a reset, which reports no old items, so the outgoing rows are detached here.
            foreach (var packageFolder in PackageFolders)
            {
                packageFolder.PropertyChanged -= PackageFolder_PropertyChanged;
            }

            PackageFolders.Clear();

            var listedFolders = config?.Packages.Folders ?? [];
            foreach (var listedFolder in listedFolders)
            {
                PackageFolders.Add(new PackageFolderViewModel
                {
                    Path = listedFolder
                });
            }
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    /// <summary>
    /// Appends a blank package folder row for the user to fill in.
    /// </summary>
    public void AddPackageFolder()
    {
        PackageFolders.Add(new PackageFolderViewModel());
    }

    /// <summary>
    /// Removes a package folder row. The folder and its packages stay on disk.
    /// </summary>
    public void RemovePackageFolder(PackageFolderViewModel packageFolder)
    {
        PackageFolders.Remove(packageFolder);
    }

    /// <summary>
    /// Replaces a package folder row's path with a folder chosen from the project's folders.
    /// </summary>
    public async Task PickPackageFolderAsync(PackageFolderViewModel packageFolder)
    {
        var pickResult = await _dialogService.ShowFolderPickerDialogAsync(ProjectSettingsLabels.PackageFolderPickerTitle);

        // The dialog reports a dismissal as a failure, so the row keeps the path it had.
        if (pickResult.IsFailure)
        {
            return;
        }

        // The project folder is written as a slash, so the row says why it cannot be used.
        var folder = pickResult.Value;
        if (folder.IsEmpty)
        {
            packageFolder.Path = "/";
            return;
        }

        packageFolder.Path = folder.Path;
    }

    /// <summary>
    /// Replaces the downloads folder with one chosen from the project's folders.
    /// </summary>
    public async Task PickDownloadsFolderAsync()
    {
        var pickResult = await _dialogService.ShowFolderPickerDialogAsync(ProjectSettingsLabels.DownloadsFolderPickerTitle);

        // The dialog reports a dismissal as a failure, so the field keeps the folder it had.
        if (pickResult.IsFailure)
        {
            return;
        }

        var folder = pickResult.Value;
        if (folder == DownloadsFolderPath.DefaultFolder)
        {
            DownloadsFolderText = string.Empty;
            return;
        }

        DownloadsFolderText = folder.Path;
    }

    partial void OnHidePatternsTextChanged(string value)
    {
        Commit();
    }

    partial void OnSearchExcludePatternsTextChanged(string value)
    {
        Commit();
    }

    partial void OnDownloadsFolderTextChanged(string value)
    {
        Commit();
    }

    // Adding and removing rows both go through the collection, so each one lands here.
    private void PackageFolders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (PackageFolderViewModel packageFolder in e.OldItems)
            {
                packageFolder.PropertyChanged -= PackageFolder_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (PackageFolderViewModel packageFolder in e.NewItems)
            {
                packageFolder.PropertyChanged += PackageFolder_PropertyChanged;
            }
        }

        UpdatePackageFolderProblems();
        Commit();
    }

    private void PackageFolder_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PackageFolderViewModel.Path))
        {
            return;
        }

        UpdatePackageFolderProblems();
        Commit();
    }

    // Sets each row's problem: the reason that loading the project would drop the row's folder. A blank row
    // is still being filled in, so it has no problem to report.
    private void UpdatePackageFolderProblems()
    {
        var usableFolders = new List<string>();
        foreach (var packageFolder in PackageFolders)
        {
            if (string.IsNullOrWhiteSpace(packageFolder.Path))
            {
                packageFolder.Problem = PackageFolderProblem.None;
                continue;
            }

            packageFolder.Problem = PackageFolderPath.FindProblem(packageFolder.Path, usableFolders, out var folder);
            if (packageFolder.Problem == PackageFolderProblem.None)
            {
                usableFolders.Add(folder.Path);
            }
        }
    }

    private static string FormatPatterns(IReadOnlyList<string>? patterns)
    {
        if (patterns is null)
        {
            return string.Empty;
        }

        return MultilineText.FormatLines(patterns);
    }

    private static IReadOnlyList<string> ParsePatterns(string text)
    {
        return MultilineText.ParseLines(text);
    }

    private static bool ContainsInvalidPattern(string text)
    {
        var lines = ParsePatterns(text);
        var compileResult = GitignoreMatcher.Compile(lines, ignoreCase: false);

        return compileResult.IsFailure;
    }

    private void Commit()
    {
        if (_suppressCommit)
        {
            return;
        }

        var hide = ParsePatterns(HidePatternsText);
        var searchExclude = ParsePatterns(SearchExcludePatternsText);
        var packageFolders = PackageFolders
            .Select(packageFolder => packageFolder.Path)
            .ToList();

        EditConfig(draft =>
        {
            draft.SetHidePatterns(hide);
            draft.SetSearchExcludePatterns(searchExclude);
            draft.SetDownloadsFolder(DownloadsFolderText);
            draft.SetPackageFolders(packageFolders);
        });
    }
}
