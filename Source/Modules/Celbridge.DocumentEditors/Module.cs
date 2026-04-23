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
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "FileViewer") },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "SceneViewer") },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "CodeEditor") },
        };
    }
}
