namespace Celbridge.Packages;

/// <summary>
/// Reader used for bundled packages. Reads come straight off disk because
/// bundled assets sit outside any IResourceRegistry root and cannot be
/// addressed by a ResourceKey. Replaced by an assembly-resource reader when
/// the bundled-from-assembly migration lands.
/// </summary>
public sealed class DirectPackageReader : IPackageReader
{
    public bool Exists(string absolutePath)
    {
        return File.Exists(absolutePath);
    }

    public Result<string> ReadAllText(string absolutePath)
    {
        try
        {
            return File.ReadAllText(absolutePath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to read file: '{absolutePath}'")
                .WithException(ex);
        }
    }

    public Result<byte[]> ReadAllBytes(string absolutePath)
    {
        try
        {
            return File.ReadAllBytes(absolutePath);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail($"Failed to read file: '{absolutePath}'")
                .WithException(ex);
        }
    }
}
