/// <summary>
/// Indicates the validation status of the component.
/// </summary>
public enum ComponentStatus
{
    /// <summary>
    /// Component is in an unknown state.
    /// </summary>
    Unknown,

    /// <summary>
    /// Component is in a valid state.
    /// </summary>
    Valid,

    /// <summary>
    /// Component is in a error state.
    /// </summary>
    Error,

    /// <summary>
    /// Component is in a valid state, but with potential issues.
    /// </summary>
    Warning
}
