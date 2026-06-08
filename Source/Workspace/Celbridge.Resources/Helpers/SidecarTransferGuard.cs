using Celbridge.DataTransfer;

namespace Celbridge.Resources.Helpers;

// Shared guard for the copy and move commands: a .cel sidecar must never be
// transferred on its own. A sidecar follows its parent automatically when the
// parent is moved or copied, so a direct move or copy of a .cel key would
// orphan or duplicate it. Returns a failed Result when the source is a sidecar
// key, null when it is a regular resource.
public static class SidecarTransferGuard
{
    public static Result? DenySidecarSource(
        ISidecarService sidecarService,
        ResourceKey source,
        DataTransferMode transferMode)
    {
        if (!sidecarService.IsSidecarKey(source))
        {
            return null;
        }

        var verb = transferMode.ToString().ToLowerInvariant();

        return Result.Fail(
            $"Cannot {verb} '{source}': the .cel extension is reserved for project metadata sidecars. "
            + "A sidecar follows its parent automatically when the parent is moved or copied.");
    }
}
