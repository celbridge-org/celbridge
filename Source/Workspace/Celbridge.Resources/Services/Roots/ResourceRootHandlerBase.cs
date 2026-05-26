using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Shared implementation for resource root handlers. Holds the backing location and
/// path validator, and provides the common Resolve and GetResourceKey logic.
/// Concrete subclasses supply the root name and capability flags.
/// </summary>
public abstract class ResourceRootHandlerBase : IResourceRootHandler
{
    private readonly PathValidator _pathValidator;
    private readonly string _backingLocation;

    protected ResourceRootHandlerBase(string backingLocation, PathValidator pathValidator)
    {
        _backingLocation = backingLocation;
        _pathValidator = pathValidator;
    }

    public abstract string RootName { get; }

    public string BackingLocation => _backingLocation;

    public abstract ResourceRootCapabilities Capabilities { get; }

    public Result<string> Resolve(ResourceKey key)
    {
        return _pathValidator.ValidateAndResolve(RootName, BackingLocation, key);
    }

    public Result<ResourceKey> GetResourceKey(string absolutePath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(absolutePath);
            var normalizedBacking = Path.GetFullPath(BackingLocation)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            bool isBackingRoot = normalizedPath.Equals(normalizedBacking, comparison);
            bool isUnderBacking = normalizedPath.StartsWith(
                normalizedBacking + Path.DirectorySeparatorChar, comparison);

            if (!isBackingRoot && !isUnderBacking)
            {
                return Result<ResourceKey>.Fail(
                    $"Path '{absolutePath}' is not under root '{RootName}' backing location '{BackingLocation}'.");
            }

            var relativePart = isBackingRoot
                ? string.Empty
                : normalizedPath
                    .Substring(normalizedBacking.Length)
                    .Replace('\\', '/')
                    .Trim('/');

            var keyString = string.IsNullOrEmpty(relativePart)
                ? RootName + ":"
                : RootName + ":" + relativePart;

            if (!ResourceKey.TryCreate(keyString, out var resourceKey))
            {
                return Result<ResourceKey>.Fail(
                    $"Path '{absolutePath}' produces an invalid resource key: '{keyString}'.");
            }

            return resourceKey;
        }
        catch (Exception ex)
        {
            return Result<ResourceKey>.Fail($"An exception occurred when getting the resource key for '{absolutePath}'.")
                .WithException(ex);
        }
    }
}
