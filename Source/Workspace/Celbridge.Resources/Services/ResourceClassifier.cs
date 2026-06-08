using Celbridge.Logging;
using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services;

public sealed class ResourceClassifier : IResourceClassifier
{
    private readonly ILogger<ResourceClassifier> _logger;

    public ResourceClassifier(ILogger<ResourceClassifier> logger)
    {
        _logger = logger;
    }

    public SidecarReport ClassifyResources(IFolderResource projectRoot, IRootHandlerRegistry rootHandlerRegistry)
    {
        var healthy = new List<ResourceKey>();
        var broken = new List<ResourceKey>();
        var orphan = new List<ResourceKey>();

        ProcessFolder(projectRoot);

        return new SidecarReport(
            Healthy: healthy,
            Broken: broken,
            Orphan: orphan);

        void ProcessFolder(IFolderResource folder)
        {
            // Sibling name lookup keeps the per-file pairing checks O(1).
            var siblingByName = new Dictionary<string, IResource>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in folder.Children)
            {
                siblingByName[child.Name] = child;
            }

            foreach (var child in folder.Children)
            {
                if (child is IFolderResource subFolder)
                {
                    ProcessFolder(subFolder);
                    continue;
                }

                if (child is not FileResource fileResource)
                {
                    continue;
                }

                ClassifyFile(fileResource, siblingByName);
            }
        }

        void ClassifyFile(FileResource fileResource, Dictionary<string, IResource> siblingByName)
        {
            var name = fileResource.Name;

            // Files ending in .cel.cel are never paired with anything. They are
            // surfaced as Broken so the user can resolve them.
            if (name.EndsWith(".cel.cel", StringComparison.OrdinalIgnoreCase))
            {
                fileResource.Sidecar = null;
                fileResource.FileKind = FileKind.InvalidSidecar;
                broken.Add(ResourceTreeNavigator.BuildKey(fileResource));
                return;
            }

            if (name.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
            {
                ClassifySidecarFile(fileResource, siblingByName);
                return;
            }

            // A non-.cel file is plain data regardless of whether a sibling
            // .cel sidecar exists for it; only its Sidecar pointer changes.
            fileResource.FileKind = FileKind.PlainData;

            var sidecarName = name + SidecarHelper.Extension;
            if (siblingByName.TryGetValue(sidecarName, out var sibling)
                && sibling is FileResource siblingFile
                && !siblingFile.Name.EndsWith(".cel.cel", StringComparison.OrdinalIgnoreCase))
            {
                var sidecarKey = ResourceTreeNavigator.BuildKey(siblingFile);

                // The sidecar's classification may not have run yet; populate a
                // placeholder Healthy entry now and let ClassifySidecarFile
                // overwrite it with the inspected status when it runs.
                var existingStatus = fileResource.Sidecar?.Status ?? CelFileStatus.Healthy;
                fileResource.Sidecar = new SidecarLink(sidecarKey, existingStatus);
                return;
            }

            fileResource.Sidecar = null;
        }

        void ClassifySidecarFile(FileResource sidecarFile, Dictionary<string, IResource> siblingByName)
        {
            var sidecarName = sidecarFile.Name;
            var parentName = sidecarName.Substring(0, sidecarName.Length - SidecarHelper.Extension.Length);
            var sidecarKey = ResourceTreeNavigator.BuildKey(sidecarFile);

            // Inspect the .cel file's content to determine its parse state.
            // Path resolution goes through the root handler registry so a
            // sidecar that resolves through a symlink or junction is rejected
            // by the same check that protects every other resource operation.
            // A failed resolve is treated as Broken — the bytes might still be
            // readable, but the rest of the system refuses to operate on them
            // and the user needs to see the file flagged for repair.
            CelFileStatus status;
            var resolveResult = rootHandlerRegistry.ResolveResourcePath(sidecarKey);
            if (resolveResult.IsFailure)
            {
                _logger.LogWarning($"sidecar pairing: failed to resolve path for '{sidecarKey}': {resolveResult.FirstErrorMessage}");
                status = CelFileStatus.Broken;
            }
            else
            {
                status = SidecarHelper.Inspect(resolveResult.Value, _logger);
            }

            // A .cel file has no sidecar of its own.
            sidecarFile.Sidecar = null;

            if (siblingByName.TryGetValue(parentName, out var parentSibling)
                && parentSibling is FileResource parentFile)
            {
                parentFile.Sidecar = new SidecarLink(sidecarKey, status);
                sidecarFile.FileKind = FileKind.Sidecar;
            }
            else
            {
                // No parent on disk. The .cel is an orphan — surface it through
                // the orphan list so the project-check reporter can prompt the
                // user to clean it up. The .cel extension is reserved for
                // sidecars; document-type registrations cannot claim it.
                sidecarFile.FileKind = FileKind.Orphan;
                orphan.Add(sidecarKey);
            }

            if (status == CelFileStatus.Healthy)
            {
                healthy.Add(sidecarKey);
            }
            else
            {
                broken.Add(sidecarKey);
            }
        }
    }
}
