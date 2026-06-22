using Celbridge.Activities;
using Celbridge.Modules;
using Celbridge.Packages;

namespace Celbridge.DocumentEditors;

public class Module : IModule
{
    private const string EditorsFolderName = "Editors";

    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        return [];
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        var editorsRoot = Path.Combine(AppContext.BaseDirectory, "Celbridge.DocumentEditors", EditorsFolderName);

        return new[]
        {
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "Notes") },
            // Editors migrated to the loopback file-server scheme (the macOS WebView hosting layer)
            // set ServedViaLoopback. The remaining editors follow; each flips the flag as it is migrated.
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "FileViewer"), ServedViaLoopback = true },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "SceneViewer") },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "CodeEditor"), ServedViaLoopback = true },
        };
    }
}
