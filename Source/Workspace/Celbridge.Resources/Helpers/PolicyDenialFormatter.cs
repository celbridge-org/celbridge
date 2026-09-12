using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Builds concise, localized reasons for a resource policy denial from the typed
/// PolicyDenialError, so user-facing surfaces never echo the engine's verbose
/// diagnostic message. The caller supplies a fallback display name, used when the
/// denial names no resource of its own.
/// </summary>
public static class PolicyDenialFormatter
{
    public static string FormatReason(Result failure, string resourceName, IStringLocalizer stringLocalizer)
    {
        if (failure.FirstException is PolicyDenialError denial)
        {
            var deniedName = denial.Resource.ResourceName;
            if (string.IsNullOrEmpty(deniedName))
            {
                deniedName = resourceName;
            }

            return stringLocalizer.GetString("Policy_Reserved_Single", deniedName);
        }

        // No typed denial attached: the remaining permissibility failure is a
        // read-only root.
        return stringLocalizer.GetString("Policy_ReadOnly_Single", resourceName);
    }
}
