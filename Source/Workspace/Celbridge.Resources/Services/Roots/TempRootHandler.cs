using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Resource root handler for the temp: virtual root. Backs scratch space and intermediate
/// artifacts under .celbridge/temp/. Host policy puts sub-folder conventions on top
/// (temp:staging/..., temp:scratch/..., temp:cache/...).
/// </summary>
public class TempRootHandler : ResourceRootHandlerBase
{
    /// <summary>
    /// The root name for the temp: virtual root.
    /// </summary>
    public const string Name = "temp";

    private static readonly ResourceRootCapabilities TempCapabilities = new(
        IsWritable: true,
        IsWatched: true);

    public override string RootName => Name;
    public override ResourceRootCapabilities Capabilities => TempCapabilities;

    public TempRootHandler(string backingLocation, PathValidator pathValidator)
        : base(backingLocation, pathValidator)
    {
    }
}
