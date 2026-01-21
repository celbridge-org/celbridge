using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Services;

public class ProjectTemplateService : IProjectTemplateService
{
    private readonly List<ProjectTemplate> _templates;

    public ProjectTemplateService(IStringLocalizer stringLocalizer)
    {
        _templates =
        [
            new ProjectTemplate
            {
                Id = "empty",
                Name = stringLocalizer.GetString("Template_Empty_Name"),
                Description = stringLocalizer.GetString("Template_Empty_Description"),
                Icon = "\uE8A5", // Document icon
                TemplateAssetPath = "ms-appx:///Assets/Templates/Empty.zip",
                TemplateProjectFileName = "empty.celbridge"
            },
            new ProjectTemplate
            {
                Id = "examples",
                Name = stringLocalizer.GetString("Template_Examples_Name"),
                Description = stringLocalizer.GetString("Template_Examples_Description"),
                Icon = "\uE736", // Library icon
                TemplateAssetPath = "ms-appx:///Assets/Templates/Examples.zip",
                TemplateProjectFileName = "examples.celbridge"
            }
        ];
    }

    public IReadOnlyList<ProjectTemplate> GetTemplates() =>
        _templates;

    public ProjectTemplate GetDefaultTemplate() =>
        _templates.First(t => t.Id == "empty");
}
