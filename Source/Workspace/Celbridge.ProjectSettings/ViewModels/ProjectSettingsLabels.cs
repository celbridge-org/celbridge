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
}
