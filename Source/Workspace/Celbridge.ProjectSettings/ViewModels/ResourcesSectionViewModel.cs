using Celbridge.Dialog;
using Celbridge.ProjectSettings.Helpers;
using Celbridge.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Resources section: the patterns the Explorer hides, the patterns search skips, and the
/// folder downloads are saved to. Each pattern list is edited as one block of text, a pattern per line, so
/// it can be pasted between projects. Neither list changes what tools may read or write, and all three
/// apply when the project is reloaded.
/// </summary>
public partial class ResourcesSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IDialogService _dialogService;

    // Set while the section rebuilds itself from the config, so populating the fields does not write what
    // it just read back into the draft.
    private bool _suppressCommit;

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
    /// True when a line of the hide list is written in a dialect the matcher does not read, so it would
    /// silently match nothing.
    /// </summary>
    public bool HasInvalidHidePattern => ContainsInvalidPattern(HidePatternsText);

    /// <summary>
    /// True when a line of the search-exclude list is written in a dialect the matcher does not read.
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

    public ResourcesSectionViewModel(
        ProjectSettingsContext context,
        IDialogService dialogService)
        : base(context)
    {
        _dialogService = dialogService;
    }

    public override void Load()
    {
        var config = GetConfig();

        _suppressCommit = true;
        HidePatternsText = FormatPatterns(config?.Resources.Hide);
        SearchExcludePatternsText = FormatPatterns(config?.Resources.SearchExclude);
        DownloadsFolderText = config?.Resources.DownloadsFolder ?? string.Empty;
        _suppressCommit = false;
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
        return ParsePatterns(text).Any(IsInvalidPattern);
    }

    // The box reads like a .gitignore, so the rest of that grammar gets typed into it. A line written in
    // one of those forms is reported rather than stored as a pattern that reads differently here. The two
    // gitignore wildcards the matcher escapes as ordinary characters are reported for the same reason.
    private static bool IsInvalidPattern(string pattern)
    {
        return pattern.Contains('\\')
            || pattern.Contains('?')
            || pattern.Contains('[')
            || pattern.StartsWith('/')
            || pattern.StartsWith('!')
            || pattern.StartsWith('#');
    }

    private void Commit()
    {
        if (_suppressCommit)
        {
            return;
        }

        var hide = ParsePatterns(HidePatternsText);
        var searchExclude = ParsePatterns(SearchExcludePatternsText);

        EditConfig(draft =>
        {
            draft.SetHidePatterns(hide);
            draft.SetSearchExcludePatterns(searchExclude);
            draft.SetDownloadsFolder(DownloadsFolderText);
        });
    }
}
