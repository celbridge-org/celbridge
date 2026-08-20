using Celbridge.Commands;

namespace Celbridge.Community;

/// <summary>
/// Opens a community link as a web view document, regenerating the document first so it always opens on
/// the catalog's page. A link whose document is already open is activated in place, leaving the page it
/// is showing untouched.
/// </summary>
public interface IOpenCommunityLinkCommand : IExecutableCommand
{
    /// <summary>
    /// The id of the community link to open.
    /// </summary>
    string LinkId { get; set; }
}
