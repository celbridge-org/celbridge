using Celbridge.Packages;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Shared localized labels used by the Project Settings item view models. The labels are bound from
/// data templates, which resolve against the item view model, so exposing them here lets each small
/// view model surface them without taking a localizer dependency of its own.
/// </summary>
internal static class ProjectSettingsLabels
{
    private static IStringLocalizer? _localizer;

    private static IStringLocalizer Localizer => _localizer ??= ServiceLocator.AcquireService<IStringLocalizer>();

    public static string StringListPlaceholder => Localizer.GetString("ProjectSettings_StringListPlaceholder");
    public static string EditorPickerTooltip => Localizer.GetString("ProjectSettings_EditorPickerTooltip");
    public static string PackageToggleTooltip => Localizer.GetString("ProjectSettings_PackageToggleTooltip");
    public static string ContributionToggleTooltip => Localizer.GetString("ProjectSettings_ContributionToggleTooltip");
    public static string PackageEnabledLabel => Localizer.GetString("ProjectSettings_PackageEnabledLabel");
    public static string ContributionEnabledLabel => Localizer.GetString("ProjectSettings_ContributionEnabledLabel");
    public static string ManifestLabel => Localizer.GetString("ProjectSettings_ManifestLabel");
    public static string OpenManifestTooltip => Localizer.GetString("ProjectSettings_OpenManifestTooltip");
    public static string FileExtensionsLabel => Localizer.GetString("ProjectSettings_FileExtensionsLabel");
    public static string DocumentTypeLabel => Localizer.GetString("ProjectSettings_ContributionType_Document");
    public static string UtilityTypeLabel => Localizer.GetString("ProjectSettings_ContributionType_Utility");

    public static string ContributionIssuesTitle => Localizer.GetString("ProjectSettings_ContributionIssuesTitle");

    public static string PackagesSectionIssue => Localizer.GetString("ProjectSettings_SectionIssue_Packages");

    public static string PackageName(string name) => Localizer.GetString("ProjectSettings_PackageNameFormat", name);

    /// <summary>
    /// Describes a contribution's dropped settings: the one issue named, or the count when there are
    /// several, so a contribution never renders a list of near-identical sentences.
    /// </summary>
    public static string ContributionIssues(IReadOnlyList<ContributionIssue> issues)
    {
        if (issues.Count == 1)
        {
            var issue = issues[0];
            return issue.Kind switch
            {
                ContributionIssueKind.UnresolvedIcon =>
                    Localizer.GetString("ProjectSettings_ContributionIssue_UnresolvedIcon_Single", issue.Value),
                _ => Localizer.GetString("ProjectSettings_ContributionIssue_Multiple", issues.Count)
            };
        }

        return Localizer.GetString("ProjectSettings_ContributionIssue_Multiple", issues.Count);
    }

    /// <summary>
    /// Describes which of a package's contributions have dropped settings: the one named, or the count.
    /// </summary>
    public static string PackageIssues(IReadOnlyList<string> contributionNames)
    {
        if (contributionNames.Count == 1)
        {
            return Localizer.GetString("ProjectSettings_PackageIssue_Single", contributionNames[0]);
        }

        return Localizer.GetString("ProjectSettings_PackageIssue_Multiple", contributionNames.Count);
    }

    public static string BuiltInPackageName(string name) => Localizer.GetString("ProjectSettings_BuiltInPackageNameFormat", name);
}
