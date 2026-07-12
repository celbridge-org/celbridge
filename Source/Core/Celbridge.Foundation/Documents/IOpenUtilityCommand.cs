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

    /// <summary>
    /// Whether to activate the document once open. True for a user-initiated launch from the toolbar;
    /// false for auto-open on project load, so a restored session's active tab is not stolen.
    /// </summary>
    bool Activate { get; set; }
}
