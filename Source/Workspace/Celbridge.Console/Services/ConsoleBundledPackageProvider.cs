using Celbridge.Packages;

namespace Celbridge.Console.Services;

/// <summary>
/// Registers the bundled console document package (the .console editor and its web assets), discovered at
/// workspace load alongside the module-contributed packages.
/// </summary>
public sealed class ConsoleBundledPackageProvider : IBundledPackageProvider
{
    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        var packageFolder = Path.Combine(AppContext.BaseDirectory, "Celbridge.Console", "Web", "Console");

        return new[]
        {
            new BundledPackageDescriptor { Folder = packageFolder }
        };
    }
}
