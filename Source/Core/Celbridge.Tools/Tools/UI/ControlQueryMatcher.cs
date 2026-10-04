using Celbridge.Automation;

namespace Celbridge.Tools;

/// <summary>
/// The fields a control lookup matches on. Each non-empty field must exactly equal the control's value. An empty
/// field matches any control.
/// </summary>
public record ControlQuery(string AutomationId, string Name, string ControlType);

/// <summary>
/// Decides whether a control lookup's query matches a control.
/// </summary>
public static class ControlQueryMatcher
{
    /// <summary>
    /// True when every field is empty, so the query would match every control.
    /// </summary>
    public static bool IsEmpty(ControlQuery query)
    {
        return string.IsNullOrEmpty(query.AutomationId) &&
            string.IsNullOrEmpty(query.Name) &&
            string.IsNullOrEmpty(query.ControlType);
    }

    /// <summary>
    /// True when every non-empty field exactly equals the control's value.
    /// </summary>
    public static bool Matches(ControlQuery query, ControlInfo control)
    {
        return FieldMatches(query.AutomationId, control.AutomationId) &&
            FieldMatches(query.Name, control.Name) &&
            FieldMatches(query.ControlType, control.ControlType);
    }

    private static bool FieldMatches(string wanted, string actual)
    {
        if (string.IsNullOrEmpty(wanted))
        {
            return true;
        }

        return string.Equals(wanted, actual, StringComparison.Ordinal);
    }
}
