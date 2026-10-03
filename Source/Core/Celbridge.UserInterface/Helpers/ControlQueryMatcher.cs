namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Decides whether a control lookup's query matches a control.
/// </summary>
public static class ControlQueryMatcher
{
    /// <summary>
    /// Whether the query names nothing to match, which would match every control.
    /// </summary>
    public static bool IsEmpty(ControlQuery query)
    {
        return string.IsNullOrEmpty(query.AutomationId) &&
            string.IsNullOrEmpty(query.Name) &&
            string.IsNullOrEmpty(query.ControlType);
    }

    /// <summary>
    /// Whether every field the query names equals the control's own value, compared exactly.
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
