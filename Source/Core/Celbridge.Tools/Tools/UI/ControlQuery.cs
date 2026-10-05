using Celbridge.Automation;

namespace Celbridge.Tools;

/// <summary>
/// The fields a control lookup matches on. Each non-empty field must exactly equal the control's value. An empty
/// field matches any control.
/// </summary>
public record ControlQuery(string AutomationId, string Name, string ControlType)
{
    /// <summary>
    /// True when every field is empty, so the query would match every control.
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(AutomationId) &&
        string.IsNullOrEmpty(Name) &&
        string.IsNullOrEmpty(ControlType);

    /// <summary>
    /// True when every non-empty field exactly equals the control's value.
    /// </summary>
    public bool Matches(ControlInfo control)
    {
        return FieldMatches(AutomationId, control.AutomationId) &&
            FieldMatches(Name, control.Name) &&
            FieldMatches(ControlType, control.ControlType);
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
