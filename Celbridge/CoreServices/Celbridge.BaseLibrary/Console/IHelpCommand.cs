using Celbridge.Commands;

namespace Celbridge.Console;

/// <summary>
/// Provides guidance on how to use the console.
/// </summary>
public interface IHelpCommand : IExecutableCommand
{
    /// <summary>
    /// Search term used to filter the displayed list of methods.
    /// Any method signature that contains the search term (case-insensitive) will be displayed.
    /// If SearchTerm is empty, then all method signatures are displayed.
    /// </summary>
    public string SearchTerm { get; set; }
}
