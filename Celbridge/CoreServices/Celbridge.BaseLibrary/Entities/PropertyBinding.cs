namespace Celbridge.Entities;

/// <summary>
/// Component property binding modes.
/// </summary>
public enum PropertyBindingMode
{
    /// <summary>
    /// Read the bound property once at initialization.
    /// </summary>
    OneTime,

    /// <summary>
    /// Read the bound property whenever its value changes.
    /// </summary>
    OneWay,

    /// <summary>
    /// Read the bound property whenever its value changes, and set the bound property
    /// whenever the UI control value changes.
    /// </summary>
    TwoWay
}

/// <summary>
/// Describes a binding between a form element and a component property.
/// </summary>
public record PropertyBinding(string PropertyPath, PropertyBindingMode BindingMode);
