namespace Celbridge.Forms;

/// <summary>
/// Provides data for a form, with support for change notification.
/// </summary>
public interface IFormDataProvider
{
    /// <summary>
    /// Callback called when the form UI is loaded.
    /// </summary>
    void OnFormLoaded();

    /// <summary>
    /// Callback called when the form UI is unloaded.
    /// </summary>
    void OnFormUnloaded();

    /// <summary>
    /// An event that fires when a property used by the form changes.
    /// The event contains the property path that changed.
    /// </summary>
    event Action<string>? FormPropertyChanged;

    /// <summary>
    /// Gets the property at the specified path as JSON.
    /// </summary>
    Result<string> GetProperty(string propertyPath);

    /// <summary>
    /// Sets the property at the specified path as JSON.
    /// </summary>
    Result SetProperty(string propertyPath, string jsonValue, bool insert = false);
}
