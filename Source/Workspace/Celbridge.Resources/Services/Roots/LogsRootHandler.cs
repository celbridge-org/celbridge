namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Resource root handler for the logs: virtual root. Backs operational diagnostic
/// output from the host, Python scripts, agents, and console session loggers
/// in the project data folder's logs/.
/// </summary>
public class LogsRootHandler : ResourceRootHandlerBase
{
    /// <summary>
    /// The root name for the logs: virtual root.
    /// </summary>
    public const string Name = "logs";

    // Not watched: the logs folder is rewritten constantly by the loggers it backs, and nothing consumes
    // change events for it today (logs are viewed through the OS file manager, not as in-app documents).
    // Watching it only produced churn, including a feedback loop where a logged exception rewrote the file
    // and triggered another notification. Python and console sessions write logs from their own processes, and
    // the resolver sees none of those changes. The root's folder listings therefore expire.
    private static readonly ResourceRootCapabilities LogsCapabilities = new(
        IsWritable: true,
        IsWatched: false,
        ListingPolicy: GetLocalListingPolicy(ListingPolicy.Expiring));

    public override string RootName => Name;
    public override ResourceRootCapabilities Capabilities => LogsCapabilities;

    public LogsRootHandler(string backingLocation)
        : base(backingLocation)
    {
    }
}
