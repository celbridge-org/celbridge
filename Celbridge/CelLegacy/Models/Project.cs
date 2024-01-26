namespace CelLegacy.Models;

// Todo: A project should be an Object, but not a Resource because a Project can't contain another Project file directly.
// Todo: A project _can_ contain a Project Reference file which links to another Project.
[ResourceType("Project", "A Celbridge project", "\uE8A1", ".celbridge")] // PreviewLink icon
public partial class Project : ObservableObject, IProject, IEntity
{
    [JsonProperty(Order = 1)]
    public Guid Id { get; set; }

    [JsonProperty(Order = 2)]
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            SetProperty(ref _name, value);
        }
    }

    public event Action? NameChanged;

    [JsonIgnore]
    public bool IsNameEditable => false;

    [JsonProperty(Order = 3)]
    private string _description = string.Empty;

    [TextAreaProperty]
    public string Description
    {
        get => _description;
        set
        {
            SetProperty(ref _description, value);
        }
    }

    public string GetKey()
    {
        return string.Empty;
    }

    [JsonIgnore]
    public string ProjectPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string ProjectFolder => Path.GetDirectoryName(ProjectPath) ?? string.Empty;

    [JsonIgnore]
    public string LibraryFolder => Path.Combine(ProjectFolder, "Library");

    private string _tooltip = string.Empty;
    [JsonIgnore]
    public string Tooltip
    {
        get => _tooltip;
        set
        {
            SetProperty(ref _tooltip, value);
        }
    }

    [JsonProperty(Order = 4)]
    public ResourceRegistry ResourceRegistry { get; set; } = new ResourceRegistry();

    public Project()
    {
        UpdateTooltip();
        PropertyChanged += Project_PropertyChanged;
    }

    private void Project_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Name):
                NameChanged?.Invoke();
                break;
            case nameof(Description):
                UpdateTooltip();
                break;
        }
    }

    private void UpdateTooltip()
    {
        if (string.IsNullOrEmpty(Description))
        {
            Tooltip = "Select project";
        }
        else
        {
            Tooltip = $"{Description}";
        }
    }
}
