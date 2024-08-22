namespace Celbridge.Utilities;

/// <summary>
/// A simple dump file utility.
/// </summary>
public interface IDumpFile
{
    /// <summary>
    /// Initialize the dump file.
    /// </summary>
    Result Initialize(string filePath);

    /// <summary>
    /// Write a line of text to the file.
    /// </summary>
    Result WriteLine(string line);

    /// <summary>
    /// Clear the contents of the file.
    /// </summary>
    Result ClearFile();
}
