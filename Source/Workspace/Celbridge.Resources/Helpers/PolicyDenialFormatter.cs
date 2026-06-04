using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Builds concise, localized reasons for a resource policy denial from the typed
/// PolicyDenialError, so user-facing surfaces never echo the engine's verbose
/// diagnostic message. The caller supplies a fallback display name; when a typed
/// denial is present the message instead names the resource the rule actually
/// matched, so a folder blocked by a locked descendant points at the descendant
/// rather than mislabelling the folder.
/// </summary>
public static class PolicyDenialFormatter
{
    public static string FormatReason(Result failure, string resourceName, IStringLocalizer stringLocalizer)
    {
        if (failure.FirstException is PolicyDenialError denial)
        {
            // Name the resource the rule matched, not the caller's target: a
            // structural change on a folder can be denied by a locked descendant,
            // so "'REPORT.md' is locked" is accurate where "'Data' is locked"
            // would be confidently wrong.
            var deniedName = denial.Resource.ResourceName;
            if (string.IsNullOrEmpty(deniedName))
            {
                deniedName = resourceName;
            }

            // Lock is the one mechanism specific enough to name; everything else
            // (ignore-file, remove list, reserved system paths) stays vague so a
            // precise-but-wrong attribution is impossible.
            return denial.MatchedRule.Source == PolicyRuleSource.ProjectLocked
                ? stringLocalizer.GetString("Policy_Locked_Single", deniedName)
                : stringLocalizer.GetString("Policy_Excluded_Single", deniedName);
        }

        // No typed denial attached: the remaining permissibility failure is a
        // read-only root.
        return stringLocalizer.GetString("Policy_ReadOnly_Single", resourceName);
    }
}
