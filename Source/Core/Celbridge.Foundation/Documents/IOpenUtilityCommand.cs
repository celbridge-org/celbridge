using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Open a utility document by its fully-qualified id, or activate it if already open. The backing file
/// is seeded from the manifest template on first open. This is the launch entry point for utilities;
/// the title-bar toolbar dispatches this command.
/// </summary>
public interface IOpenUtilityCommand : IExecutableCommand
{
    /// <summary>
    /// The fully-qualified id of the utility to open, in "{packageName}.{documentId}" form.
    /// </summary>
    string UtilityId { get; set; }
}
