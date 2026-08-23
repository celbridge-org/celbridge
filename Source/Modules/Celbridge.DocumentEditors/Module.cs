using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.ProjectSettings;
using Celbridge.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.DocumentEditors;

public class Module : IModule
{
    public void ConfigureServices(IModuleServiceCollection services)
    {
        services.AddSingleton<IBundledPackageProvider, DocumentEditorsBundledPackageProvider>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        var stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        var projectService = serviceProvider.GetRequiredService<IProjectService>();
        return
        [
            new ProjectSettingsEditorFactory(stringLocalizer, projectService),
            new PackageManifestFactory(stringLocalizer),
            new EditorManifestFactory(stringLocalizer),
        ];
    }
}
