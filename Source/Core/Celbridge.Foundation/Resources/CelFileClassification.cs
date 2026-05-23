using Celbridge.Documents;

namespace Celbridge.Resources;

/// <summary>
/// How a .cel file is classified relative to other resources and the
/// registered document editor factories.
/// </summary>
public enum CelFileClassification
{
    /// <summary>
    /// The .cel file is a standalone form recognised by a registered factory
    /// (e.g. .project.cel, .mod.cel). It owns its own content and is not
    /// paired with a parent file.
    /// </summary>
    Standalone,

    /// <summary>
    /// The .cel file pairs with a sibling parent file in the same folder.
    /// Parent existence wins: a paired sidecar is classified as Sidecar even
    /// when its multi-part extension also matches a registered factory.
    /// </summary>
    Sidecar,

    /// <summary>
    /// The .cel file has no sibling parent and no factory claims its
    /// multi-part extension. Surfaced by project-health checks for the user
    /// to repair.
    /// </summary>
    Orphan,
}

/// <summary>
/// Classifies a .cel file as Standalone, Sidecar, or Orphan by consulting the
/// resource registry (for parent existence) and the document editor registry
/// (for registered multi-part extensions). The single helper keeps the two
/// dimensions of "is this a sidecar?" and "is this a standalone form?" in one
/// place so callers do not reinvent the precedence rule.
/// </summary>
public static class CelFileClassifier
{
    private const string CelExtension = ".cel";

    /// <summary>
    /// Classify a .cel resource. Parent existence wins: if a sibling parent
    /// file exists in the same folder, the result is Sidecar regardless of
    /// whether the multi-part extension is also registered. Otherwise the
    /// multi-part extension lookup decides between Standalone and Orphan.
    /// </summary>
    public static CelFileClassification Classify(
        ResourceKey key,
        IResourceRegistry resources,
        IDocumentEditorRegistry editors)
    {
        Guard.IsNotNull(resources);
        Guard.IsNotNull(editors);

        var path = key.Path;
        if (string.IsNullOrEmpty(path)
            || !path.EndsWith(CelExtension, StringComparison.OrdinalIgnoreCase))
        {
            return CelFileClassification.Orphan;
        }

        // Parent existence wins: if removing the .cel suffix names a sibling
        // file that exists in the registry, the .cel file is the sidecar for
        // that parent. This holds even when the resulting multi-part extension
        // also matches a registered factory.
        var parentPath = path.Substring(0, path.Length - CelExtension.Length);
        if (!string.IsNullOrEmpty(parentPath))
        {
            var parentKey = new ResourceKey(key.Root + ":" + parentPath);
            var parentResult = resources.GetResource(parentKey);
            if (parentResult.IsSuccess
                && parentResult.Value is IFileResource)
            {
                return CelFileClassification.Sidecar;
            }
        }

        // Compute the multi-part extension - the suffix from the last interior
        // dot in the file name. For meeting.note.cel this is ".note.cel"; for
        // foo.cel it is just ".cel".
        var multiPartExtension = GetMultiPartExtension(key);
        if (!string.IsNullOrEmpty(multiPartExtension))
        {
            var factories = editors.GetFactoriesForFileExtension(multiPartExtension);
            if (factories.Count > 0)
            {
                return CelFileClassification.Standalone;
            }
        }

        return CelFileClassification.Orphan;
    }

    private static string GetMultiPartExtension(ResourceKey key)
    {
        var fileName = key.ResourceName;
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        // Skip a leading '.' so dotfiles like ".cel" still expose ".cel" rather
        // than the whole name.
        int searchFrom = fileName.Length > 0 && fileName[0] == '.' ? 1 : 0;

        var trimmed = fileName.Substring(searchFrom);
        var firstDot = trimmed.IndexOf('.');
        if (firstDot < 0)
        {
            return string.Empty;
        }

        // The interior segment may itself contain multiple dots (e.g.
        // foo.bar.baz.cel produces ".bar.baz.cel"). Resolution picks the
        // longest interior suffix that names a real .cel form; the registry's
        // longest-match walk handles the rest.
        var multiPart = trimmed.Substring(firstDot).ToLowerInvariant();
        return multiPart;
    }
}
