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
                Id = "Empty",
                Name = stringLocalizer.GetString("Template_Empty_Name"),
                Description = stringLocalizer.GetString("Template_Empty_Description"),
                Icon = "\uE8A5" // Document icon
            },
            new ProjectTemplate
            {
                Id = "Examples",
                Name = stringLocalizer.GetString("Template_Examples_Name"),
                Description = stringLocalizer.GetString("Template_Examples_Description"),
                Icon = "\uE736" // Library icon
            }
        ];
    }

    public IReadOnlyList<ProjectTemplate> GetTemplates() =>
        _templates;

    public ProjectTemplate GetDefaultTemplate() =>
        _templates.First(t => t.Id == "Empty");
}
