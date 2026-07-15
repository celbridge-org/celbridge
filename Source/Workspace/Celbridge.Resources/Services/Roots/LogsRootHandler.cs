namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Resource root handler for the logs: virtual root. Backs operational diagnostic
/// output from the host, Python scripts, agents, and console session loggers
/// under .celbridge/logs/.
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
    // and triggered another notification.
    private static readonly ResourceRootCapabilities LogsCapabilities = new(
        IsWritable: true,
        IsWatched: false);

    public override string RootName => Name;
    public override ResourceRootCapabilities Capabilities => LogsCapabilities;

    public LogsRootHandler(string backingLocation)
        : base(backingLocation)
    {
    }
}
