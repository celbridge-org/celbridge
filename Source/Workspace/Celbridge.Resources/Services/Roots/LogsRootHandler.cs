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

    private static readonly ResourceRootCapabilities LogsCapabilities = new(
        IsWritable: true,
        IsWatched: true);

    public override string RootName => Name;
    public override ResourceRootCapabilities Capabilities => LogsCapabilities;

    public LogsRootHandler(string backingLocation)
        : base(backingLocation)
    {
    }
}
