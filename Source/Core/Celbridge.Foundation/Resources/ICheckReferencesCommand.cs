using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single project: reference that does not resolve to an existing resource. Site is where the
/// reference literal sits; MissingTarget is the resource key the literal points to.
/// </summary>
public record BrokenReference(ResourceReferenceSite Site, ResourceKey MissingTarget)
{
    /// <summary>
    /// The file holding the reference literal.
    /// </summary>
    public ResourceKey Source => Site.Source;
}

/// <summary>
/// The outcome of a reference check: the references that did not resolve, and how many distinct
/// targets were checked to find them.
/// </summary>
public record CheckReferencesReport(
    IReadOnlyList<BrokenReference> BrokenReferences,
    int CheckedTargetCount);

/// <summary>
/// Scans the project's text files for project: references and reports the ones that do not resolve.
/// The scan is lexical, so a reference literal quoted as an example is indistinguishable from a live one.
/// </summary>
public interface ICheckReferencesCommand : IExecutableCommand<CheckReferencesReport>
{
    /// <summary>
    /// When true, the findings are also written as a report document and opened. Callers that consume
    /// the result value directly leave this false.
    /// </summary>
    bool OpenReport { get; set; }
}
