namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Resource root handler for the utils: virtual root. Backs the persistent state of utility
/// documents under .celbridge/utils/. Unlike temp:, this root is never wiped on workspace load,
/// so a utility's state survives across sessions. The root is hidden from the Explorer, search,
/// and New File, and is ungoverned by resource policy.
/// </summary>
public class UtilsRootHandler : ResourceRootHandlerBase
{
    /// <summary>
    /// The root name for the utils: virtual root.
    /// </summary>
    public const string Name = "utils";

    private static readonly ResourceRootCapabilities UtilsCapabilities = new(
        IsWritable: true,
        IsWatched: true);

    public override string RootName => Name;
    public override ResourceRootCapabilities Capabilities => UtilsCapabilities;

    public UtilsRootHandler(string backingLocation)
        : base(backingLocation)
    {
    }
}
