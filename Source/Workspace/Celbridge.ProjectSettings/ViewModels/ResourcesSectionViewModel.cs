using Celbridge.ProjectSettings.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Resources section: the patterns the Explorer hides and the patterns search skips. Each list
/// is edited as one block of text, a pattern per line, so it can be pasted between projects. Neither list
/// changes what tools may read or write, and both apply when the project is reloaded.
/// </summary>
public partial class ResourcesSectionViewModel : ProjectSettingsSectionViewModel
{
    // Set while the section rebuilds itself from the config, so populating the fields does not write what
    // it just read back into the draft.
    private bool _suppressCommit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalidHidePattern))]
    private string _hidePatternsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalidSearchExcludePattern))]
    private string _searchExcludePatternsText = string.Empty;

    /// <summary>
    /// True when a line of the hide list is written in a dialect the matcher does not read, so it would
    /// silently match nothing.
    /// </summary>
    public bool HasInvalidHidePattern => ContainsInvalidPattern(HidePatternsText);

    /// <summary>
    /// True when a line of the search-exclude list is written in a dialect the matcher does not read.
    /// </summary>
    public bool HasInvalidSearchExcludePattern => ContainsInvalidPattern(SearchExcludePatternsText);

    public string ProtectedNote => ProjectSettingsLabels.ResourcesProtectedNote;
    public string HideTitle => ProjectSettingsLabels.HideTitle;
    public string HideSubtitle => ProjectSettingsLabels.HideSubtitle;
    public string SearchExcludeTitle => ProjectSettingsLabels.SearchExcludeTitle;
    public string SearchExcludeSubtitle => ProjectSettingsLabels.SearchExcludeSubtitle;
    public string PatternPlaceholder => ProjectSettingsLabels.PatternPlaceholder;
    public string InvalidPatternText => ProjectSettingsLabels.InvalidPattern;

    public ResourcesSectionViewModel(ProjectSettingsContext context)
        : base(context)
    {
    }

    public override void Load()
    {
        var config = GetConfig();

        _suppressCommit = true;
        HidePatternsText = FormatPatterns(config?.Resources.Hide);
        SearchExcludePatternsText = FormatPatterns(config?.Resources.SearchExclude);
        _suppressCommit = false;
    }

    partial void OnHidePatternsTextChanged(string value)
    {
        Commit();
    }

    partial void OnSearchExcludePatternsTextChanged(string value)
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
        });
    }
}
