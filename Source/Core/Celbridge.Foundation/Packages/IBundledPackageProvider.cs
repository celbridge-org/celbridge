namespace Celbridge.Packages;

/// <summary>
/// A DI-registered source of bundled package descriptors, discovered at workspace load alongside the
/// module-contributed packages. Lets a core project own and register its own bundled package without being
/// a module.
/// </summary>
public interface IBundledPackageProvider
{
    /// <summary>
    /// Returns descriptors for the bundled packages this provider contributes, or an empty list if none.
    /// </summary>
    IReadOnlyList<BundledPackageDescriptor> GetBundledPackages();
}
