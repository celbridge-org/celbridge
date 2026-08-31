using Celbridge.Packages;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Which localized message describes a set of issues: the resource key to look up and the value
/// formatted into it. Choosing the message is separated from looking it up so the choice can be tested
/// without a localizer.
/// </summary>
internal sealed record IssueMessage(string ResourceKey, string Argument);

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
    public static string RevealManifestTooltip => Localizer.GetString("ProjectSettings_RevealManifestTooltip");
    public static string PageLocationLabel => Localizer.GetString("ProjectSettings_PageLocationLabel");
    public static string PageManifestIssueTitle => Localizer.GetString("ProjectSettings_PageManifestIssueTitle");
    public static string PageManifestIssue => Localizer.GetString("ProjectSettings_PageManifestIssue");
    public static string PagesEmpty => Localizer.GetString("ProjectSettings_PagesEmpty");
    public static string FileExtensionsLabel => Localizer.GetString("ProjectSettings_FileExtensionsLabel");
    public static string ShortcutsEmpty => Localizer.GetString("ProjectSettings_ShortcutsEmpty");
    public static string AddShortcut => Localizer.GetString("ProjectSettings_AddShortcut");
    public static string ShortcutUntitled => Localizer.GetString("ProjectSettings_ShortcutUntitled");
    public static string ShortcutResourceLabel => Localizer.GetString("ProjectSettings_ShortcutResourceLabel");
    public static string ShortcutResourcePlaceholder => Localizer.GetString("ProjectSettings_ShortcutResourcePlaceholder");
    public static string ShortcutResourceHint => Localizer.GetString("ProjectSettings_ShortcutResourceHint");
    public static string ShortcutInvalidResource => Localizer.GetString("ProjectSettings_ShortcutInvalidResource");
    public static string ShortcutMissingResource => Localizer.GetString("ProjectSettings_ShortcutMissingResource");
    public static string ShortcutBrowseTooltip => Localizer.GetString("ProjectSettings_ShortcutBrowseTooltip");
    public static string ShortcutPickerTitle => Localizer.GetString("ProjectSettings_ShortcutPickerTitle");
    public static string ShortcutOpenTooltip => Localizer.GetString("ProjectSettings_ShortcutOpenTooltip");
    public static string ShortcutAreaLabel => Localizer.GetString("ProjectSettings_ShortcutAreaLabel");
    public static string ShortcutAreaHint => Localizer.GetString("ProjectSettings_ShortcutAreaHint");
    public static string ShortcutIconLabel => Localizer.GetString("ProjectSettings_ShortcutIconLabel");
    public static string ShortcutIconPlaceholder => Localizer.GetString("ProjectSettings_ShortcutIconPlaceholder");
    public static string ShortcutIconHint => Localizer.GetString("ProjectSettings_ShortcutIconHint");
    public static string ShortcutUnknownIcon => Localizer.GetString("ProjectSettings_ShortcutUnknownIcon");
    public static string DocumentTypeLabel => Localizer.GetString("ProjectSettings_ContributionType_Document");
    public static string UtilityTypeLabel => Localizer.GetString("ProjectSettings_ContributionType_Utility");

    public static string ContributionIssuesTitle => Localizer.GetString("ProjectSettings_ContributionIssuesTitle");

    public static string PackagesSectionIssue => Localizer.GetString("ProjectSettings_SectionIssue_Packages");

    /// <summary>
    /// The display name of a workspace area, as the shortcut area picker lists it.
    /// </summary>
    public static string WorkspaceAreaName(WorkspaceArea area)
    {
        return Localizer.GetString($"ProjectSettings_WorkspaceArea_{area}");
    }

    public static string PackageName(string name) => Localizer.GetString("ProjectSettings_PackageNameFormat", name);

    public static string PackageVersion(int version) => Localizer.GetString("ProjectSettings_PackageVersionFormat", version);

    public static string FeatureFlagOn => Localizer.GetString("ProjectSettings_FeatureFlagState_On");

    public static string FeatureFlagOff => Localizer.GetString("ProjectSettings_FeatureFlagState_Off");

    /// <summary>
    /// The "Default (On)" / "Default (Off)" label for the inherit option, naming the value the flag
    /// currently resolves to.
    /// </summary>
    public static string FeatureFlagDefault(bool applicationValue)
    {
        string stateLabel;
        if (applicationValue)
        {
            stateLabel = FeatureFlagOn;
        }
        else
        {
            stateLabel = FeatureFlagOff;
        }

        return Localizer.GetString("ProjectSettings_FeatureFlagState_DefaultFormat", stateLabel);
    }

    /// <summary>
    /// Describes a contribution's dropped settings: the one issue named, or the count when there are
    /// several, so a contribution never renders a list of near-identical sentences.
    /// </summary>
    public static string ContributionIssues(IReadOnlyList<ContributionIssue> issues)
    {
        var message = ContributionIssueMessage(issues);

        return Localizer.GetString(message.ResourceKey, message.Argument);
    }

    /// <summary>
    /// Describes which of a package's contributions have dropped settings: the one named, or the count.
    /// </summary>
    public static string PackageIssues(IReadOnlyList<string> contributionNames)
    {
        var message = PackageIssueMessage(contributionNames);

        return Localizer.GetString(message.ResourceKey, message.Argument);
    }

    /// <summary>
    /// Chooses the message for a contribution's dropped settings. A single issue is named by the value
    /// that could not be applied; several are reported as a count, which also covers a mix of kinds.
    /// </summary>
    internal static IssueMessage ContributionIssueMessage(IReadOnlyList<ContributionIssue> issues)
    {
        if (issues.Count == 1)
        {
            var issue = issues[0];
            if (issue.Kind == ContributionIssueKind.UnresolvedIcon)
            {
                return new IssueMessage("ProjectSettings_ContributionIssue_UnresolvedIcon_Single", issue.Value);
            }
        }

        return new IssueMessage("ProjectSettings_ContributionIssue_Multiple", issues.Count.ToString());
    }

    /// <summary>
    /// Chooses the message naming which of a package's contributions have dropped settings.
    /// </summary>
    internal static IssueMessage PackageIssueMessage(IReadOnlyList<string> contributionNames)
    {
        if (contributionNames.Count == 1)
        {
            return new IssueMessage("ProjectSettings_PackageIssue_Single", contributionNames[0]);
        }

        return new IssueMessage("ProjectSettings_PackageIssue_Multiple", contributionNames.Count.ToString());
    }

    public static string BuiltInPackageName(string name) => Localizer.GetString("ProjectSettings_BuiltInPackageNameFormat", name);
}
