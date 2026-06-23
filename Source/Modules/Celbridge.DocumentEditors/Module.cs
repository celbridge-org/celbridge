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
            // Editors migrated to the loopback file-server scheme (the macOS WebView hosting layer)
            // set ServedViaLoopback, which routes them over the WebSocket host channel. SpreadJS is the
            // last remaining editor (it needs the native synthetic-origin shim) and stays on the
            // virtual-host WebView2 transport on Windows until that path lands.
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "Notes"), ServedViaLoopback = true },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "FileViewer"), ServedViaLoopback = true },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "SceneViewer"), ServedViaLoopback = true },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "CodeEditor"), ServedViaLoopback = true },
        };
    }
}
