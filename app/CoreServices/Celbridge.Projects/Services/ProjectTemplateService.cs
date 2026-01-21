using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Services;

public class ProjectTemplateService : IProjectTemplateService
{
    private readonly List<ProjectTemplate> _templates;

    public ProjectTemplateService(IStringLocalizer stringLocalizer)
    {
        _templates =
        [
            CreateTemplate(stringLocalizer, "Empty", "\uE8A5"),
            CreateTemplate(stringLocalizer, "Examples", "\uE736")
        ];
    }

    private static ProjectTemplate CreateTemplate(IStringLocalizer stringLocalizer, string id, string icon)
    {
        return new ProjectTemplate
        {
            Id = id,
            Name = stringLocalizer.GetString($"Template_{id}_Name"),
            Description = stringLocalizer.GetString($"Template_{id}_Description"),
            Icon = icon
        };
    }

    public IReadOnlyList<ProjectTemplate> GetTemplates() =>
        _templates;

    public ProjectTemplate GetDefaultTemplate() =>
        _templates.First(t => t.Id == "Empty");
}
