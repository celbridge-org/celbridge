using Celbridge.Resources;
using Microsoft.Extensions.Localization;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Resolves the localised read-only message for a WritableState, or null when
/// the state is Writable. Centralised so a single resw key is bound per state.
/// </summary>
public static class ReadOnlyMessageHelper
{
    /// <summary>
    /// Returns the localised read-only message for the writable state, or null
    /// when the resource is Writable. Bindings that consume this should treat
    /// null as "no tooltip, no automation help text" so the tooltip element
    /// collapses on writable resources.
    /// </summary>
    public static string? GetReadOnlyMessage(WritableState state, IStringLocalizer localizer)
    {
        var key = state switch
        {
            WritableState.Locked => "Resource_ReadOnly_Locked",
            WritableState.ReadOnlyAttribute => "Resource_ReadOnly_ReadOnlyAttribute",
            WritableState.ReadOnlyRoot => "Resource_ReadOnly_ReadOnlyRoot",
            _ => null,
        };

        if (key is null)
        {
            return null;
        }

        return localizer.GetString(key).Value;
    }
}
