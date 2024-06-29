namespace Celbridge.BaseLibrary.Utilities;

/// <summary>
/// Provides access to common low-level utility methods.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Returns true if the string represents a valid path segment on the current platform.
    /// </summary>
    bool IsPathSegmentValid(string segment);
}
