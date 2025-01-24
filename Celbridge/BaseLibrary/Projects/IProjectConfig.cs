/// <summary>
/// Interface for project configuration management.
/// </summary>
public interface IProjectConfig
{
    /// <summary>
    /// Gets the value of a property by its name. If the property does not exist, returns the specified default value.
    /// </summary>
    string GetProperty(string propertyName, string defaultValue);

    /// <summary>
    /// Gets the value of a property by its name. If the property does not exist, returns an empty string.
    /// </summary>
    string GetProperty(string propertyName);

    /// <summary>
    /// Sets the value of a property by its name.
    /// </summary>
    void SetProperty(string propertyName, string jsonEncodedValue);

    /// <summary>
    /// Checks if a property exists by its name.
    /// </summary>
    bool HasProperty(string propertyName);
}
