using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class ResourceClassifier : IResourceClassifier
{
    private readonly ILogger<ResourceClassifier> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceClassifier(
        ILogger<ResourceClassifier> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public ResourceClassificationResult ClassifyResources(IFolderResource projectRoot, IRootHandlerRegistry rootHandlerRegistry)
    {
        var healthy = new List<ResourceKey>();
        var broken = new List<ResourceKey>();
        var orphan = new List<ResourceKey>();
        var sidecarToParent = new Dictionary<ResourceKey, ResourceKey>();

        var editorRegistry = ResolveEditorRegistry();

        ProcessFolder(projectRoot);

        var report = new CelFileReport(
            Healthy: healthy,
            Broken: broken,
            Orphan: orphan);

        return new ResourceClassificationResult(report, sidecarToParent);

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
                sidecarToParent[sidecarKey] = ResourceTreeNavigator.BuildKey(parentFile);
                parentFile.Sidecar = new SidecarLink(sidecarKey, status);
                sidecarFile.FileKind = FileKind.Sidecar;
            }
            else
            {
                // No parent: either a registered standalone .cel form
                // (e.g. foo.webview.cel, foo.note.cel) or a true orphan.
                // Standalone forms are matched via the editor registry and
                // must not appear in the orphan list.
                if (IsRegisteredStandaloneCelForm(sidecarKey, editorRegistry))
                {
                    sidecarFile.FileKind = FileKind.Standalone;
                }
                else
                {
                    sidecarFile.FileKind = FileKind.Orphan;
                    orphan.Add(sidecarKey);
                }
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

    private IDocumentEditorRegistry ResolveEditorRegistry()
    {
        // WorkspaceService is populated before the page UI loads, so this is safe
        // during workspace load. The property throws if no service is present.
        return _workspaceWrapper.WorkspaceService.DocumentsService.DocumentEditorRegistry;
    }

    // Checks whether a parentless .cel file is claimed by a registered factory
    // in a way that denotes a standalone form. The match shape that counts
    // here is a multi-part extension suffix that includes a segment in front
    // of ".cel" (e.g. ".webview.cel", ".note.cel"). Exact-filename matches are
    // also accepted for completeness, though no current factory registers a
    // bare .cel filename. The bare ".cel" extension is excluded: it also
    // serves the generic code-editor syntax-highlighting registration, which
    // says nothing about pairing semantics. Without that exclusion every
    // parentless ".cel" would silently disappear from the orphan report.
    private static bool IsRegisteredStandaloneCelForm(
        ResourceKey sidecarKey,
        IDocumentEditorRegistry editorRegistry)
    {
        var fileName = sidecarKey.ResourceName;

        if (editorRegistry.IsFilenameSupported(fileName))
        {
            return true;
        }

        foreach (var suffix in EnumerateExtensionSuffixes(fileName))
        {
            if (string.Equals(suffix, ".cel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (editorRegistry.IsExtensionSupported(suffix))
            {
                return true;
            }
        }

        return false;
    }

    // Yields each extension suffix of a filename from longest to shortest.
    // Mirrors the walk in DocumentEditorRegistry so the pairing check sees the
    // same suffix set as the editor open path. A leading dot is skipped so a
    // dotfile is not treated as one giant extension.
    private static IEnumerable<string> EnumerateExtensionSuffixes(string fileName)
    {
        int searchFrom = 0;
        if (fileName.Length > 0
            && fileName[0] == '.')
        {
            searchFrom = 1;
        }

        while (searchFrom < fileName.Length)
        {
            int dotIndex = fileName.IndexOf('.', searchFrom);
            if (dotIndex < 0)
            {
                yield break;
            }

            yield return fileName.Substring(dotIndex);
            searchFrom = dotIndex + 1;
        }
    }
}
